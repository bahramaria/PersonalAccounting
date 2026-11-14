using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class BackupService(
    IDbContextFactory<AppDbContext> contextFactory,
    string databasePath,
    TimeProvider timeProvider)
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("HESABYAR-BAKUP-1\n");
    private const int FormatVersion = 1;
    private const int KdfIterations = 600_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumBackupSize = 512 * 1024 * 1024;
    private const string DatabaseEntryName = "data.db";
    private const string ManifestEntryName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateBackupAsync(
        string destinationPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!string.IsNullOrEmpty(password) && password.Length < 8)
            throw new ArgumentException("Backup password must contain at least 8 characters.", nameof(password));

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        Directory.CreateDirectory(destinationDirectory);
        var snapshotPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.snapshot");
        try
        {
            await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
            var databaseBytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken);
            var schemaVersion = await GetSchemaVersionAsync(cancellationToken);
            var manifest = new BackupManifest(
                FormatVersion,
                "0.1.0",
                schemaVersion,
                timeProvider.GetUtcNow(),
                databaseBytes.LongLength,
                Convert.ToHexString(SHA256.HashData(databaseBytes)));
            using var archiveStream = new MemoryStream();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (var entryStream = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions, cancellationToken);
                var databaseEntry = archive.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
                await using var databaseStream = databaseEntry.Open();
                await databaseStream.WriteAsync(databaseBytes, cancellationToken);
            }

            var archiveBytes = archiveStream.ToArray();
            var encrypted = !string.IsNullOrEmpty(password);
            var header = new BackupEnvelopeHeader(encrypted, encrypted ? KdfIterations : 0, null, null, null);
            byte[] payload = archiveBytes;
            if (encrypted)
            {
                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var tag = new byte[TagSize];
                var key = Rfc2898DeriveBytes.Pbkdf2(password!, salt, KdfIterations, HashAlgorithmName.SHA256, 32);
                var cipher = new byte[archiveBytes.Length];
                try
                {
                    using var aes = new AesGcm(key, TagSize);
                    aes.Encrypt(nonce, archiveBytes, cipher, tag);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
                header = new BackupEnvelopeHeader(true, KdfIterations,
                    Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
                payload = cipher;
            }

            var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
            var temporaryOutputPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporaryOutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await output.WriteAsync(Magic, cancellationToken);
                    await output.WriteAsync(BitConverter.GetBytes(headerBytes.Length), cancellationToken);
                    await output.WriteAsync(headerBytes, cancellationToken);
                    await output.WriteAsync(payload, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }
                File.Move(temporaryOutputPath, destinationPath, overwrite: true);
                return destinationPath;
            }
            finally
            {
                if (File.Exists(temporaryOutputPath)) File.Delete(temporaryOutputPath);
            }
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    public async Task<BackupSummary> InspectBackupAsync(
        string backupPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var archiveBytes = await ReadArchivePayloadAsync(backupPath, password, cancellationToken);
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = await ReadManifestAsync(archive, cancellationToken);
        var database = await ReadDatabaseAsync(archive, manifest, cancellationToken);
        await ValidateDatabaseAsync(database, manifest.SchemaVersion, cancellationToken);
        await ValidateSchemaVersionAsync(manifest.SchemaVersion, cancellationToken);
        return new BackupSummary(manifest.CreatedAtUtc, manifest.AppVersion, manifest.SchemaVersion,
            manifest.DatabaseSizeBytes, archiveBytes.Length, IsEncrypted(backupPath));
    }

    public async Task<string> RestoreBackupAsync(
        string backupPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var archiveBytes = await ReadArchivePayloadAsync(backupPath, password, cancellationToken);
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = await ReadManifestAsync(archive, cancellationToken);
        var databaseBytes = await ReadDatabaseAsync(archive, manifest, cancellationToken);
        await ValidateDatabaseAsync(databaseBytes, manifest.SchemaVersion, cancellationToken);
        await ValidateSchemaVersionAsync(manifest.SchemaVersion, cancellationToken);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(directory);
        var safetyBackup = Path.Combine(directory,
            $"pre-restore-{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}.hbak");
        await CreateBackupAsync(safetyBackup, cancellationToken: cancellationToken);

        var replacementPath = Path.Combine(directory, $".{Guid.NewGuid():N}.restore");
        try
        {
            await File.WriteAllBytesAsync(replacementPath, databaseBytes, cancellationToken);
            SqliteConnection.ClearAllPools();
            DeleteSqliteSidecars();
            File.Move(replacementPath, databasePath, overwrite: true);
            return safetyBackup;
        }
        finally
        {
            if (File.Exists(replacementPath)) File.Delete(replacementPath);
        }
    }

    public async Task CreateDailyAutomaticBackupAsync(
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var today = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        if (Directory.GetFiles(backupDirectory, $"automatic-{today}-*.hbak").Length > 0)
            return;

        var path = Path.Combine(backupDirectory, $"automatic-{today}-{Guid.NewGuid():N}.hbak");
        await CreateBackupAsync(path, cancellationToken: cancellationToken);
        var backups = Directory.GetFiles(backupDirectory, "automatic-*.hbak")
            .Select(file => new FileInfo(file))
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToArray();
        foreach (var oldBackup in backups.Skip(7))
            oldBackup.Delete();
    }

    private async Task CreateDatabaseSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("The application database does not exist.", databasePath);

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private async Task<string> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var migrations = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        return migrations.LastOrDefault() ?? "0";
    }

    private async Task ValidateSchemaVersionAsync(string schemaVersion, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var supported = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        if (schemaVersion != "0" && !supported.Contains(schemaVersion))
            throw new InvalidDataException("The backup was created by a newer or unsupported database schema.");
    }

    private async Task<byte[]> ReadArchivePayloadAsync(
        string backupPath,
        string? password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var file = new FileInfo(backupPath);
        if (!file.Exists) throw new FileNotFoundException("Backup file was not found.", backupPath);
        if (file.Length > MaximumBackupSize || file.Length < Magic.Length + sizeof(int))
            throw new InvalidDataException("Backup file size is invalid.");

        var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken);
        if (!bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("The file is not a supported Hesabyar backup.");

        var headerLength = BitConverter.ToInt32(bytes, Magic.Length);
        var payloadOffset = Magic.Length + sizeof(int) + headerLength;
        if (headerLength <= 0 || headerLength > 4096 || payloadOffset >= bytes.Length)
            throw new InvalidDataException("Backup header is invalid.");

        var header = JsonSerializer.Deserialize<BackupEnvelopeHeader>(
            bytes.AsSpan(Magic.Length + sizeof(int), headerLength), JsonOptions)
            ?? throw new InvalidDataException("Backup header is invalid.");
        var payload = bytes.AsSpan(payloadOffset).ToArray();
        if (!header.Encrypted)
            return payload;
        if (string.IsNullOrEmpty(password))
            throw new UnauthorizedAccessException("This backup requires its password.");
        if (header.Iterations < KdfIterations || header.Iterations > 2_000_000)
            throw new InvalidDataException("Backup key derivation parameters are invalid.");

        try
        {
            var salt = Convert.FromBase64String(header.Salt ?? "");
            var nonce = Convert.FromBase64String(header.Nonce ?? "");
            var tag = Convert.FromBase64String(header.Tag ?? "");
            if (salt.Length != SaltSize || nonce.Length != NonceSize || tag.Length != TagSize)
                throw new InvalidDataException("Backup encryption metadata is invalid.");
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, header.Iterations, HashAlgorithmName.SHA256, 32);
            var plaintext = new byte[payload.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, payload, tag, plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            throw new UnauthorizedAccessException("The backup password is incorrect or the file was modified.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Backup encryption metadata is invalid.", exception);
        }
    }

    private static async Task<BackupManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entries = archive.Entries;
        if (entries.Count != 2 || entries.Count(x => x.FullName == ManifestEntryName) != 1 ||
            entries.Count(x => x.FullName == DatabaseEntryName) != 1)
            throw new InvalidDataException("Backup archive contents are invalid.");
        var entry = archive.GetEntry(ManifestEntryName)!;
        await using var input = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(input, JsonOptions, cancellationToken);
        if (manifest is null || manifest.FormatVersion != FormatVersion || manifest.DatabaseSizeBytes <= 0)
            throw new InvalidDataException("Backup manifest is invalid or unsupported.");
        return manifest;
    }

    private static async Task<byte[]> ReadDatabaseAsync(
        ZipArchive archive,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(DatabaseEntryName)!;
        if (entry.Length != manifest.DatabaseSizeBytes || entry.Length > MaximumBackupSize)
            throw new InvalidDataException("Backup database size does not match its manifest.");
        await using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        await input.CopyToAsync(output, cancellationToken);
        var database = output.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(database));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(checksum), Convert.FromHexString(manifest.DatabaseSha256)))
            throw new InvalidDataException("Backup database checksum validation failed.");
        return database;
    }

    private static async Task ValidateDatabaseAsync(byte[] database, string expectedSchemaVersion, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"hesabyar-validate-{Guid.NewGuid():N}.db");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, database, cancellationToken);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup database integrity check failed.");
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("Backup does not contain a Hesabyar database.");
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
            var actualSchemaVersion = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "0";
            if (!string.Equals(actualSchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
                throw new InvalidDataException("Backup schema version does not match its manifest.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("Backup database is not a valid SQLite database.", exception);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private bool IsEncrypted(string backupPath)
    {
        using var stream = File.OpenRead(backupPath);
        stream.Position = Magic.Length;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var headerLength = reader.ReadInt32();
        if (headerLength <= 0 || headerLength > 4096) throw new InvalidDataException("Backup header is invalid.");
        var headerBytes = reader.ReadBytes(headerLength);
        var header = JsonSerializer.Deserialize<BackupEnvelopeHeader>(headerBytes, JsonOptions);
        return header?.Encrypted ?? throw new InvalidDataException("Backup header is invalid.");
    }

    private void DeleteSqliteSidecars()
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed record BackupManifest(
        int FormatVersion,
        string AppVersion,
        string SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        long DatabaseSizeBytes,
        string DatabaseSha256);

    private sealed record BackupEnvelopeHeader(
        bool Encrypted,
        int Iterations,
        string? Salt,
        string? Nonce,
        string? Tag);
}

public sealed record BackupSummary(
    DateTimeOffset CreatedAtUtc,
    string AppVersion,
    string SchemaVersion,
    long DatabaseSizeBytes,
    long BackupFileSizeBytes,
    bool IsEncrypted);
