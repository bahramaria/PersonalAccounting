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
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await incorrect.OpenAsync();
                await using var invalidCommand = incorrect.CreateCommand();
                invalidCommand.CommandText = "SELECT Value FROM SecretData;";
                await invalidCommand.ExecuteScalarAsync();
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VacuumInto_CanExportBetweenPlaintextAndSqlCipherV4()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var plainPath = Path.Combine(directory, "plain.db");
        var encryptedPath = Path.Combine(directory, "encrypted.db");
        var exportedPath = Path.Combine(directory, "exported.db");
        const string password = "vacuum-into-device-key";
        try
        {
            await using (var plain = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = plainPath, Pooling = false
            }.ToString()))
            {
                await plain.OpenAsync();
                await using var create = plain.CreateCommand();
                create.CommandText = "CREATE TABLE Data (Value TEXT NOT NULL); INSERT INTO Data VALUES ('roundtrip');";
                await create.ExecuteNonQueryAsync();
                await using var export = plain.CreateCommand();
                export.CommandText = "VACUUM INTO $destination;";
                export.Parameters.AddWithValue("$destination",
                    SqliteConnectionStringFactory.CreateSqlCipherV4Uri(encryptedPath, password));
                await export.ExecuteNonQueryAsync();
            }

            var encryptedConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = encryptedPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString(), password);
            await using (var encrypted = new SqliteConnection(encryptedConnectionString))
            {
                await encrypted.OpenAsync();
                await using var export = encrypted.CreateCommand();
                export.CommandText = "VACUUM INTO $destination;";
                export.Parameters.AddWithValue("$destination",
                    SqliteConnectionStringFactory.CreateSqlCipherV4Uri(exportedPath, password));
                await export.ExecuteNonQueryAsync();
            }

            var exportedEncryptedConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
                new SqliteConnectionStringBuilder
                {
                    DataSource = exportedPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false
                }.ToString(), password);
            await using (var exportedEncrypted = new SqliteConnection(exportedEncryptedConnectionString))
            {
                await exportedEncrypted.OpenAsync();
                await using var decrypt = exportedEncrypted.CreateCommand();
                decrypt.CommandText = "PRAGMA rekey = '';";
                await decrypt.ExecuteNonQueryAsync();
            }

            var exportedHeader = new byte[16];
            await using (var stream = File.OpenRead(exportedPath))
                await stream.ReadExactlyAsync(exportedHeader);
            Assert.True(exportedHeader.AsSpan().SequenceEqual("SQLite format 3\\0"u8));

            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = exportedPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString());
            await verify.OpenAsync();
            await using var query = verify.CreateCommand();
            query.CommandText = "SELECT Value FROM Data;";
            Assert.Equal("roundtrip", await query.ExecuteScalarAsync());
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
