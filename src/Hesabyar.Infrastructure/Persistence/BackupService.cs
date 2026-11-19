using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class BackupService(
    IDbContextFactory<AppDbContext> contextFactory,
    string databasePath,
    TimeProvider timeProvider,
    IDatabaseKeyProvider? keyProvider = null,
    IBackupFileOperations? fileOperations = null,
    string? attachmentsDirectory = null)
{
    private readonly IBackupFileOperations _fileOperations = fileOperations ?? new BackupFileOperations();
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("HESABYAR-BAKUP-1
");
    private const int FormatVersion = 3;
    private const int KdfIterations = 600_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumBackupSize = 512 * 1024 * 1024;
    private const string DatabaseEntryName = "data.db";
    private const string AttachmentEntryPrefix = "attachments/";
    private readonly string? _attachmentsDirectory = attachmentsDirectory;
    private const string ManifestEntryName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateBackupAsync(
        string destinationPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!string.IsNullOrEmpty(password) &&
            (password.Length < 8 || string.IsNullOrWhiteSpace(password)))
            throw new ArgumentException("Backup password must contain at least 8 non-whitespace characters.", nameof(password));

        var privateDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(privateDirectory);
        var snapshotPath = Path.Combine(privateDirectory, $".{Guid.NewGuid():N}.snapshot");
        try
        {
            await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
            if (new FileInfo(snapshotPath).Length > MaximumBackupSize)
                throw new InvalidDataException("Database snapshot exceeds the maximum backup size.");
            var databaseBytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken);
            var schemaVersion = await GetSchemaVersionAsync(cancellationToken);
            var attachmentFiles = await ReadAttachmentFilesForBackupAsync(cancellationToken);
            var manifest = new BackupManifest(
                FormatVersion,
                "0.1.0",
                schemaVersion,
                timeProvider.GetUtcNow(),
                databaseBytes.LongLength,
                Convert.ToHexString(SHA256.HashData(databaseBytes)),
                DatabaseEncrypted: keyProvider is not null,
                DatabaseKey: keyProvider is not null && !string.IsNullOrEmpty(password)
                    ? keyProvider.GetDatabasePassword()
                    : null,
                DeviceBound: keyProvider is not null && string.IsNullOrEmpty(password),
                Attachments: attachmentFiles.Select(x => new BackupAttachment(
                    x.Name, x.Bytes.LongLength, Convert.ToHexString(SHA256.HashData(x.Bytes)))).ToArray());
            using var archiveStream = new MemoryStream();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (var entryStream = manifestEntry.Open())
                    await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions, cancellationToken);
                var databaseEntry = archive.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
                await using var databaseStream = databaseEntry.Open();
                await databaseStream.WriteAsync(databaseBytes, cancellationToken);
                foreach (var attachment in attachmentFiles)
                {
                    var entry = archive.CreateEntry(AttachmentEntryPrefix + attachment.Name, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(attachment.Bytes, cancellationToken);
                }
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
            var outputSize = (long)Magic.Length + sizeof(int) + headerBytes.Length + payload.Length;
            if (outputSize > MaximumBackupSize)
                throw new InvalidDataException("Created backup exceeds the maximum backup size.");
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
        var databasePassword = ResolveDatabasePassword(manifest);
        await ValidateDatabaseAsync(database, manifest.SchemaVersion, databasePassword, cancellationToken);
        await ValidateSchemaVersionAsync(manifest.SchemaVersion, cancellationToken);
        return new BackupSummary(manifest.CreatedAtUtc, manifest.AppVersion, manifest.SchemaVersion,
            manifest.DatabaseSizeBytes, archiveBytes.Length, IsEncrypted(backupPath), manifest.DeviceBound);
    }

    public async Task<string?> RestoreBackupAsync(
        string backupPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var archiveBytes = await ReadArchivePayloadAsync(backupPath, password, cancellationToken);
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = await ReadManifestAsync(archive, cancellationToken);
        var databaseBytes = await ReadDatabaseAsync(archive, manifest, cancellationToken);
        var sourceDatabasePassword = ResolveDatabasePassword(manifest);
        if (keyProvider is not null && sourceDatabasePassword is not null)
            restoredAttachments = restoredAttachments.Select(x => new BackupAttachmentBytes(
                x.Name, ReencryptAttachment(x.Bytes, sourceDatabasePassword, keyProvider.GetDatabasePassword()))).ToArray();
        await ValidateDatabaseAsync(databaseBytes, manifest.SchemaVersion, sourceDatabasePassword, cancellationToken);
        await ValidateSchemaVersionAsync(manifest.SchemaVersion, cancellationToken);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(directory);
        string? safetyBackup = null;
        var currentDatabaseBackupFailed = false;
        if (File.Exists(databasePath))
        {
            safetyBackup = Path.Combine(directory,
                $"pre-restore-{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}.hbak");
            try
            {
                await CreateBackupAsync(safetyBackup, keyProvider?.GetDatabasePassword(), cancellationToken);
            }
            catch (SqliteException)
            {
                // Preserve an unreadable database for investigation and continue with the validated backup.
                currentDatabaseBackupFailed = true;
                safetyBackup = null;
            }
        }

        var restoreId = Guid.NewGuid().ToString("N");
        var replacementPath = Path.Combine(directory, $".{restoreId}.restore");
        var rollbackDatabasePath = Path.Combine(directory, $".{restoreId}.rollback");
        var attachmentStagePath = _attachmentsDirectory is null ? null : _attachmentsDirectory + "." + restoreId + ".stage";
        var attachmentRollbackPath = _attachmentsDirectory is null ? null : _attachmentsDirectory + "." + restoreId + ".rollback";
        var attachmentsMovedToRollback = false;
        var replacementAttachmentsInstalled = false;
        var rollbackSidecars = new List<(string OriginalPath, string RollbackPath)>();
        var databaseMovedToRollback = false;
        var replacementInstalled = false;
        try
        {
            await CreateRestoreDatabaseAsync(replacementPath, databaseBytes, sourceDatabasePassword, cancellationToken);
            if (attachmentStagePath is not null)
            {
                Directory.CreateDirectory(attachmentStagePath);
                foreach (var attachment in restoredAttachments)
                    await File.WriteAllBytesAsync(Path.Combine(attachmentStagePath, attachment.Name), attachment.Bytes, cancellationToken);
            }
            SqliteConnection.ClearAllPools();

            try
            {
                if (_attachmentsDirectory is not null && attachmentStagePath is not null && attachmentRollbackPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_attachmentsDirectory))!);
                    if (Directory.Exists(_attachmentsDirectory))
                    {
                        Directory.Move(_attachmentsDirectory, attachmentRollbackPath);
                        attachmentsMovedToRollback = true;
                    }
                    Directory.Move(attachmentStagePath, _attachmentsDirectory);
                    replacementAttachmentsInstalled = true;
                }
                if (File.Exists(databasePath))
                {
                    _fileOperations.Move(databasePath, rollbackDatabasePath);
                    databaseMovedToRollback = true;
                }

                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecarPath = databasePath + suffix;
                    if (!File.Exists(sidecarPath)) continue;
                    var rollbackPath = rollbackDatabasePath + suffix;
                    _fileOperations.Move(sidecarPath, rollbackPath);
                    rollbackSidecars.Add((sidecarPath, rollbackPath));
                }

                _fileOperations.Move(replacementPath, databasePath);
                replacementInstalled = true;
            }
            catch (Exception restoreException)
            {
                try
                {
                    // Never delete the original database when the replacement was not installed.
                    if (replacementInstalled && File.Exists(databasePath)) File.Delete(databasePath);
                    if (replacementAttachmentsInstalled && _attachmentsDirectory is not null && Directory.Exists(_attachmentsDirectory))
                        Directory.Delete(_attachmentsDirectory, recursive: true);
                    if (attachmentsMovedToRollback && attachmentRollbackPath is not null && Directory.Exists(attachmentRollbackPath))
                        Directory.Move(attachmentRollbackPath, _attachmentsDirectory!);

                    foreach (var sidecar in rollbackSidecars.AsEnumerable().Reverse())
                    {
                        if (File.Exists(sidecar.RollbackPath))
                            _fileOperations.Move(sidecar.RollbackPath, sidecar.OriginalPath);
                    }

                    if (databaseMovedToRollback && File.Exists(rollbackDatabasePath))
                        _fileOperations.Move(rollbackDatabasePath, databasePath);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "Database restore failed and the original database could not be fully reinstated. The safety backup is preserved at: " + safetyBackup,
                        restoreException, rollbackException);
                }

                throw;
            }

            try
            {
                if (attachmentRollbackPath is not null && Directory.Exists(attachmentRollbackPath))
                    Directory.Delete(attachmentRollbackPath, recursive: true);
                if (currentDatabaseBackupFailed && databaseMovedToRollback)
                {
                    var preservedPath = Path.Combine(directory,
                        $"recovery-preserved-{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}-{restoreId}.db");
                    _fileOperations.Move(rollbackDatabasePath, preservedPath);
                    foreach (var sidecar in rollbackSidecars)
                    {
                        if (!File.Exists(sidecar.RollbackPath)) continue;
                        var suffix = sidecar.OriginalPath.EndsWith("-wal", StringComparison.Ordinal) ? "-wal" : "-shm";
                        _fileOperations.Move(sidecar.RollbackPath, preservedPath + suffix);
                    }
                }
                else
                {
                    if (File.Exists(rollbackDatabasePath)) File.Delete(rollbackDatabasePath);
                    foreach (var sidecar in rollbackSidecars)
                        if (File.Exists(sidecar.RollbackPath)) File.Delete(sidecar.RollbackPath);
                }
            }
            catch (IOException)
            {
                // A completed restore remains successful if temporary rollback cleanup is deferred.
            }
            catch (UnauthorizedAccessException)
            {
                // A completed restore remains successful if temporary rollback cleanup is deferred.
            }

            return safetyBackup;
        }
        finally
        {
            if (File.Exists(replacementPath)) File.Delete(replacementPath);
            if (attachmentStagePath is not null && Directory.Exists(attachmentStagePath))
                Directory.Delete(attachmentStagePath, recursive: true);
        }
    }

    public async Task CreateDailyAutomaticBackupAsync(
        string backupDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var today = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var existingBackups = Directory.GetFiles(backupDirectory, $"automatic-{today}-*.hbak");
        if (existingBackups.Any(path =>
            {
                try { return IsEncrypted(path); }
                catch (InvalidDataException) { return false; }
            }))
            return;

        var path = Path.Combine(backupDirectory, $"automatic-{today}-{Guid.NewGuid():N}.hbak");
        await CreateBackupAsync(path, keyProvider?.GetDatabasePassword(), cancellationToken);
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
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false
        }.ToString();
        if (keyProvider is not null)
            sourceConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                sourceConnectionString, keyProvider.GetDatabasePassword());

        await using var source = new SqliteConnection(sourceConnectionString);
        await source.OpenAsync(cancellationToken);
        var destinationUri = keyProvider is null
            ? SqliteConnectionStringFactory.CreatePlainSqliteUri(snapshotPath)
            : SqliteConnectionStringFactory.CreateSqlCipherV4Uri(snapshotPath, keyProvider.GetDatabasePassword());
        await using var command = source.CreateCommand();
        command.CommandText = "VACUUM INTO $destination;";
        command.Parameters.AddWithValue("$destination", destinationUri);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private async Task CreateRestoreDatabaseAsync(
        string replacementPath, byte[] databaseBytes, string? sourceDatabasePassword,
        CancellationToken cancellationToken)
    {
        var sourcePath = replacementPath + ".source";
        try
        {
            await File.WriteAllBytesAsync(sourcePath, databaseBytes, cancellationToken);
            if (keyProvider is null)
            {
                if (sourceDatabasePassword is not null)
                    throw new InvalidDataException("An encrypted backup requires an initialized database key provider.");
                File.Move(sourcePath, replacementPath);
                return;
            }

            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString();
            if (sourceDatabasePassword is not null)
                sourceConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                    sourceConnectionString, sourceDatabasePassword);

            await using var source = new SqliteConnection(sourceConnectionString);
            await source.OpenAsync(cancellationToken);
            var destinationUri = SqliteConnectionStringFactory.CreateSqlCipherV4Uri(
                replacementPath, keyProvider.GetDatabasePassword());
            await using var command = source.CreateCommand();
            command.CommandText = "VACUUM INTO $destination;";
            command.Parameters.AddWithValue("$destination", destinationUri);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
        }
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
        if (entries.Count(x => x.FullName == ManifestEntryName) != 1 ||
            entries.Count(x => x.FullName == DatabaseEntryName) != 1 ||
            entries.Any(x => x.FullName != ManifestEntryName && x.FullName != DatabaseEntryName &&
                !x.FullName.StartsWith(AttachmentEntryPrefix, StringComparison.Ordinal)))
            throw new InvalidDataException("Backup archive contents are invalid.");
        var entry = archive.GetEntry(ManifestEntryName)!;
        await using var input = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(input, JsonOptions, cancellationToken);
        if (manifest is null || (manifest.FormatVersion < 1 || manifest.FormatVersion > FormatVersion) ||
            manifest.DatabaseSizeBytes <= 0)
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


    private static byte[] ReencryptAttachment(byte[] payload, string sourcePassword, string targetPassword)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        if (payload.Length < 1 + nonceLength + tagLength || payload[0] != 1)
            throw new InvalidDataException("Attachment encryption format is invalid.");

        var sourceKey = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePassword));
        var plaintext = new byte[payload.Length - 1 - nonceLength - tagLength];
        try
        {
            using (var sourceAes = new AesGcm(sourceKey, tagLength))
                sourceAes.Decrypt(payload.AsSpan(1, nonceLength),
                    payload.AsSpan(1 + nonceLength + tagLength),
                    payload.AsSpan(1 + nonceLength, tagLength), plaintext);

            var targetKey = SHA256.HashData(Encoding.UTF8.GetBytes(targetPassword));
            try
            {
                var nonce = RandomNumberGenerator.GetBytes(nonceLength);
                var tag = new byte[tagLength];
                var ciphertext = new byte[plaintext.Length];
                using (var targetAes = new AesGcm(targetKey, tagLength))
                    targetAes.Encrypt(nonce, plaintext, ciphertext, tag);
                return [1, .. nonce, .. tag, .. ciphertext];
            }
            finally { CryptographicOperations.ZeroMemory(targetKey); }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Backup attachment could not be decrypted with its source database key.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sourceKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<IReadOnlyList<BackupAttachmentBytes>> ReadAttachmentEntriesAsync(
        ZipArchive archive, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var declared = manifest.Attachments ?? [];
        if (declared.Count > 1000) throw new InvalidDataException("Backup contains too many attachments.");
        var entries = archive.Entries.Where(x => x.FullName.StartsWith(AttachmentEntryPrefix, StringComparison.Ordinal)).ToArray();
        if (entries.Length != declared.Count) throw new InvalidDataException("Backup attachment entries do not match the manifest.");
        var result = new List<BackupAttachmentBytes>(declared.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var item in declared)
        {
            if (Path.GetFileName(item.StoredFileName) != item.StoredFileName ||
                !item.StoredFileName.EndsWith(".hatt", StringComparison.Ordinal) ||
                item.SizeBytes <= 0 || item.SizeBytes > 15 * 1024 * 1024 ||
                !names.Add(item.StoredFileName))
                throw new InvalidDataException("Backup attachment metadata is invalid.");
            var entryName = AttachmentEntryPrefix + item.StoredFileName;
            var entry = archive.GetEntry(entryName) ?? throw new InvalidDataException("Backup attachment is missing.");
            if (entry.Length != item.SizeBytes || entry.Length > 15 * 1024 * 1024)
                throw new InvalidDataException("Backup attachment size is invalid.");
            await using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            await input.CopyToAsync(output, cancellationToken);
            var bytes = output.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(item.Sha256)))
                throw new InvalidDataException("Backup attachment checksum validation failed.");
            totalBytes += bytes.LongLength;
            if (totalBytes > MaximumBackupSize) throw new InvalidDataException("Backup attachments exceed the maximum size.");
            result.Add(new BackupAttachmentBytes(item.StoredFileName, bytes));
        }
        return result;
    }

    private async Task<IReadOnlyList<BackupAttachmentBytes>> ReadAttachmentFilesForBackupAsync(CancellationToken cancellationToken)
    {
        if (_attachmentsDirectory is null || !Directory.Exists(_attachmentsDirectory))
            return [];
        var files = Directory.GetFiles(_attachmentsDirectory, "*.hatt");
        if (files.Length > 1000) throw new InvalidDataException("Too many attachment files.");
        var result = new List<BackupAttachmentBytes>(files.Length);
        long total = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (Path.GetFileNameWithoutExtension(name).Length != 32)
                throw new InvalidDataException("Attachment storage file name is invalid.");
            var info = new FileInfo(file);
            if (info.Length <= 0 || info.Length > 15 * 1024 * 1024)
                throw new InvalidDataException("Attachment file size is invalid.");
            total += info.Length;
            if (total > MaximumBackupSize) throw new InvalidDataException("Attachments exceed the maximum backup size.");
            result.Add(new BackupAttachmentBytes(name, await File.ReadAllBytesAsync(file, cancellationToken)));
        }
        return result;
    }

    private sealed record BackupAttachment(string StoredFileName, long SizeBytes, string Sha256);
    private sealed record BackupAttachmentBytes(string Name, byte[] Bytes);

    private async Task ValidateDatabaseAsync(
        byte[] database, string expectedSchemaVersion, string? databasePassword,
        CancellationToken cancellationToken)
    {
        var privateDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        Directory.CreateDirectory(privateDirectory);
        var temporaryPath = Path.Combine(privateDirectory, $".{Guid.NewGuid():N}.validate.db");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, database, cancellationToken);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString();
            if (databasePassword is not null)
                connectionString = SqliteConnectionStringFactory.WithSqlCipherV4(connectionString, databasePassword);
            await using var connection = new SqliteConnection(connectionString);
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

    private string? ResolveDatabasePassword(BackupManifest manifest)
    {
        if (!manifest.DatabaseEncrypted) return null;
        if (manifest.DeviceBound)
        {
            if (manifest.DatabaseKey is not null)
                throw new InvalidDataException("A device-bound backup must not expose its database key.");
            if (keyProvider is not null) return keyProvider.GetDatabasePassword();
        }
        else if (!string.IsNullOrWhiteSpace(manifest.DatabaseKey) && manifest.DatabaseKey.Length <= 256)
        {
            return manifest.DatabaseKey;
        }
        throw new InvalidDataException("The backup database key is missing or the backup is device-bound.");
    }

    private sealed record BackupManifest(
        int FormatVersion,
        string AppVersion,
        string SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        long DatabaseSizeBytes,
        string DatabaseSha256,
        bool DatabaseEncrypted = false,
        string? DatabaseKey = null,
        bool DeviceBound = false,
        IReadOnlyList<BackupAttachment>? Attachments = null);

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
    bool IsEncrypted,
    bool IsDeviceBound = false);
