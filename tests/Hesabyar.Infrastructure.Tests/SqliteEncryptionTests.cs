using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class SqliteEncryptionTests
{
    [Fact]
    public async Task SqlCipherV4_EncryptsDatabase_AndRejectsWrongPassword()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "encrypted.db");
        const string password = "device-key-for-sqlcipher-test";
        try
        {
            var connectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
                }.ToString(), password);
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE SecretData (Value TEXT NOT NULL); INSERT INTO SecretData VALUES ('financial data');";
                await command.ExecuteNonQueryAsync();
            }

            var header = new byte[16];
            await using (var stream = File.OpenRead(path)) await stream.ReadExactlyAsync(header);
            Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\0"u8));

            await using (var correct = new SqliteConnection(connectionString))
            {
                await correct.OpenAsync();
                await using var command = correct.CreateCommand();
                command.CommandText = "SELECT Value FROM SecretData;";
                Assert.Equal("financial data", await command.ExecuteScalarAsync());
            }

            var wrongPassword = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), "incorrect-password");
            await using var incorrect = new SqliteConnection(wrongPassword);
            await incorrect.OpenAsync();
            await using var invalidCommand = incorrect.CreateCommand();
            invalidCommand.CommandText = "SELECT Value FROM SecretData;";
            await Assert.ThrowsAsync<SqliteException>(() => invalidCommand.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EfCoreConnectionInterceptor_AppliesDeviceKeyBeforeOpeningConnection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ef-encrypted.db");
        const string password = "device-key-for-ef-test";
        try
        {
            var plainConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
            }.ToString();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(plainConnectionString)
                .AddInterceptors(new SqliteConnectionPragmaInterceptor(new TestDatabaseKeyProvider(password)))
                .Options;
            await using (var db = new AppDbContext(options))
                await db.Database.EnsureCreatedAsync();

            var encryptedConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), password);
            await using var verify = new SqliteConnection(encryptedConnectionString);
            await verify.OpenAsync();
            await using var command = verify.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            Assert.True(Convert.ToInt64(await command.ExecuteScalarAsync()) > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestDatabaseKeyProvider(string password) : IDatabaseKeyProvider
    {
        public string GetDatabasePassword() => password;
    }
}
