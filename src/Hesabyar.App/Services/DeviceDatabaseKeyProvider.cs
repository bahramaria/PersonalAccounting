using System.Security.Cryptography;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Storage;

namespace Hesabyar.App.Services;

public sealed class DeviceDatabaseKeyProvider(string databasePath) : IDatabaseKeyProvider
{
    private const string SecureStorageKey = "database.encryption.key.v1";
    private string? _password;

    public async Task InitializeAsync(string migrationBackupDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationBackupDirectory);
        var password = await SecureStorage.Default.GetAsync(SecureStorageKey);
        if (string.IsNullOrWhiteSpace(password))
        {
            if (File.Exists(databasePath) && !await IsPlainDatabaseAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The encrypted database key is unavailable. Restore a Hesabyar backup to continue.");
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await SecureStorage.Default.SetAsync(SecureStorageKey, password);
        }

        _password = password;
        if (!File.Exists(databasePath)) return;
        if (await IsEncryptedDatabaseAsync(password, cancellationToken)) return;
        if (!await IsPlainDatabaseAsync(cancellationToken))
            throw new InvalidOperationException(
                "The database cannot be opened with the stored encryption key and is not a valid plaintext database.");
        await EncryptLegacyDatabaseAsync(password, migrationBackupDirectory, cancellationToken);
    }

    public string GetDatabasePassword() =>
        _password ?? throw new InvalidOperationException("The database encryption key has not been initialized.");

    private async Task<bool> IsEncryptedDatabaseAsync(string password, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), password);
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (SqliteException) { return false; }
    }

    private async Task<bool> IsPlainDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (SqliteException) { return false; }
    }

    private async Task EncryptLegacyDatabaseAsync(
        string password, string migrationBackupDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(migrationBackupDirectory);
        var temporaryPath = databasePath + "." + Guid.NewGuid().ToString("N") + ".encrypted";
        var safetyBackupPath = Path.Combine(migrationBackupDirectory,
            $"pre-sqlcipher-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.db");
        try
        {
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false
            }.ToString()))
            await using (var encryptedTarget = new SqliteConnection(SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
                }.ToString(), password)))
            await using (var encryptedBackup = new SqliteConnection(SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = safetyBackupPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
                }.ToString(), password)))
            {
                await source.OpenAsync(cancellationToken);
                await encryptedTarget.OpenAsync(cancellationToken);
                await encryptedBackup.OpenAsync(cancellationToken);
                await using (var checkpoint = source.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    await checkpoint.ExecuteNonQueryAsync(cancellationToken);
                }
                source.BackupDatabase(encryptedTarget);
                source.BackupDatabase(encryptedBackup);
                await using var integrity = encryptedTarget.CreateCommand();
                integrity.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals((string?)await integrity.ExecuteScalarAsync(cancellationToken), "ok",
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The encrypted database migration failed integrity validation.");
            }

            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = databasePath + suffix;
                if (File.Exists(sidecar)) File.Delete(sidecar);
            }
            File.Move(temporaryPath, databasePath, overwrite: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
