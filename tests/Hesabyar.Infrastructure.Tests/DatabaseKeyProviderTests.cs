using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;

namespace Hesabyar.Infrastructure.Tests;

public sealed class DatabaseKeyProviderTests
{
    [Fact]
    public async Task InitializeAsync_EncryptsLegacyDatabase_AndKeepsEncryptedSafetyBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarKeyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "legacy.db");
        var backupDirectory = Path.Combine(directory, "migration-backups");
        var secretStore = new InMemorySecretStore();

        try
        {
            await using (var legacy = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, Pooling = false
            }.ToString()))
            {
                await legacy.OpenAsync();
                await using var command = legacy.CreateCommand();
                command.CommandText = "CREATE TABLE LegacyData (Value TEXT NOT NULL); INSERT INTO LegacyData VALUES ('preserved');";
                await command.ExecuteNonQueryAsync();
            }

            var provider = new DatabaseKeyProvider(databasePath, secretStore);
            await provider.InitializeAsync(backupDirectory);
            var password = provider.GetDatabasePassword();
            Assert.Equal(32, Convert.FromBase64String(password).Length);
            Assert.Equal(password, await secretStore.GetAsync("database.encryption.key.v1"));

            var header = new byte[16];
            await using (var stream = File.OpenRead(databasePath)) await stream.ReadExactlyAsync(header);
            Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\0"u8));

            var encryptedConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), password);
            await using (var encrypted = new SqliteConnection(encryptedConnectionString))
            {
                await encrypted.OpenAsync();
                await using var query = encrypted.CreateCommand();
                query.CommandText = "SELECT Value FROM LegacyData;";
                Assert.Equal("preserved", await query.ExecuteScalarAsync());
            }

            var safetyBackup = Assert.Single(Directory.GetFiles(backupDirectory, "pre-sqlcipher-*.db"));
            var safetyConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = safetyBackup, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), password);
            await using var backup = new SqliteConnection(safetyConnectionString);
            await backup.OpenAsync();
            await using var backupQuery = backup.CreateCommand();
            backupQuery.CommandText = "SELECT Value FROM LegacyData;";
            Assert.Equal("preserved", await backupQuery.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotReplaceMissingKeyForEncryptedDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarKeyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "encrypted.db");
        const string originalPassword = "original-device-key";
        var encryptedConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString(), originalPassword);
        try
        {
            await using (var encrypted = new SqliteConnection(encryptedConnectionString))
            {
                await encrypted.OpenAsync();
                await using var command = encrypted.CreateCommand();
                command.CommandText = "CREATE TABLE ProtectedData (Value TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync();
            }

            var secretStore = new InMemorySecretStore();
            var provider = new DatabaseKeyProvider(databasePath, secretStore);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.InitializeAsync(Path.Combine(directory, "migration-backups")));
            Assert.Null(await secretStore.GetAsync("database.encryption.key.v1"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class InMemorySecretStore : IDeviceSecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
