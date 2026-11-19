using System.Security.Cryptography;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task EncryptedBackup_CanBeInspectedAndRestored_WithSafetyBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "manual.hbak");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount { Name = "Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }
            await service.CreateBackupAsync(backupPath, "correct horse battery");
            var summary = await service.InspectBackupAsync(backupPath, "correct horse battery");
            Assert.True(summary.IsEncrypted);
            Assert.True(summary.DatabaseSizeBytes > 0);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.InspectBackupAsync(backupPath, "incorrect password"));
            await using (var db = factory.CreateDbContext())
            {
                db.Accounts.RemoveRange(db.Accounts);
                await db.SaveChangesAsync();
            }
            await File.WriteAllTextAsync(databasePath + "-wal", "stale WAL sidecar");
            await File.WriteAllTextAsync(databasePath + "-shm", "stale SHM sidecar");
            var safetyBackup = await service.RestoreBackupAsync(backupPath, "correct horse battery");
            Assert.True(File.Exists(safetyBackup));
            Assert.False(File.Exists(databasePath + "-wal"));
            Assert.False(File.Exists(databasePath + "-shm"));
            await using (var db = factory.CreateDbContext())
                Assert.Equal("Cash", await db.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EncryptedDatabase_BackupRestore_WorksAcrossDeviceKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var restoredPath = Path.Combine(directory, "restored.db");
        var backupPath = Path.Combine(directory, "portable.hbak");
        const string sourcePassword = "source-device-key";
        const string targetPassword = "target-device-key";
        var sourceKey = new TestDatabaseKeyProvider(sourcePassword);
        var targetKey = new TestDatabaseKeyProvider(targetPassword);
        var sourceConnectionString = new SqliteConnectionStringBuilder
        { DataSource = sourcePath, ForeignKeys = true, Pooling = false }.ToString();
        var targetConnectionString = new SqliteConnectionStringBuilder
        { DataSource = restoredPath, ForeignKeys = true, Pooling = false }.ToString();
        var sourceFactory = new TestDbContextFactory(sourceConnectionString, sourceKey);
        var targetFactory = new TestDbContextFactory(targetConnectionString, targetKey);
        var sourceService = new BackupService(sourceFactory, sourcePath, TimeProvider.System, sourceKey);
        var targetService = new BackupService(targetFactory, restoredPath, TimeProvider.System, targetKey);
        try
        {
            await using (var db = sourceFactory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                { Name = "Portable Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }
            await sourceService.CreateBackupAsync(backupPath, "portable backup password");

            await using (var db = targetFactory.CreateDbContext())
                await db.Database.MigrateAsync();
            await targetService.RestoreBackupAsync(backupPath, "portable backup password");

            var header = new byte[16];
            await using (var stream = File.OpenRead(restoredPath)) await stream.ReadExactlyAsync(header);
            Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\\0"u8));
            await using var restored = targetFactory.CreateDbContext();
            Assert.Equal("Portable Cash", await restored.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task RestoreBackup_WhenCurrentDatabaseIsCorrupt_RestoresAndPreservesCorruptFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "recovery.hbak");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                { Name = "Recovered Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }

            await service.CreateBackupAsync(backupPath, "recovery password");
            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(databasePath, "corrupt sqlite content");

            var safetyBackup = await service.RestoreBackupAsync(backupPath, "recovery password");

            Assert.Null(safetyBackup);
            var preservedPath = Assert.Single(Directory.GetFiles(directory, "recovery-preserved-*.db"));
            Assert.Equal("corrupt sqlite content", await File.ReadAllTextAsync(preservedPath));
            await using var restored = factory.CreateDbContext();
            Assert.Equal("Recovered Cash", await restored.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreBackup_WhenCurrentDatabaseIsMissing_RestoresWithoutSafetyBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "recovery.hbak");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                { Name = "Recovered Cash", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }

            await service.CreateBackupAsync(backupPath, "recovery password");
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);

            var safetyBackup = await service.RestoreBackupAsync(backupPath, "recovery password");

            Assert.Null(safetyBackup);
            await using var restored = factory.CreateDbContext();
            Assert.Equal("Recovered Cash", await restored.Accounts.Select(x => x.Name).SingleAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PasswordlessEncryptedBackup_IsDeviceBoundToItsSourceKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "device-bound.hbak");
        var sourceKey = new TestDatabaseKeyProvider("source-device-key");
        var targetKey = new TestDatabaseKeyProvider("target-device-key");
        var sourceFactory = new TestDbContextFactory(new SqliteConnectionStringBuilder
        { DataSource = sourcePath, Pooling = false }.ToString(), sourceKey);
        var targetFactory = new TestDbContextFactory(new SqliteConnectionStringBuilder
        { DataSource = targetPath, Pooling = false }.ToString(), targetKey);
        var sourceService = new BackupService(sourceFactory, sourcePath, TimeProvider.System, sourceKey);
        var targetService = new BackupService(targetFactory, targetPath, TimeProvider.System, targetKey);
        try
        {
            await using (var db = sourceFactory.CreateDbContext()) await db.Database.MigrateAsync();
            await sourceService.CreateBackupAsync(backupPath);
            var summary = await sourceService.InspectBackupAsync(backupPath);
            Assert.False(summary.IsEncrypted);
            Assert.True(summary.IsDeviceBound);

            await using (var db = targetFactory.CreateDbContext()) await db.Database.MigrateAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => targetService.InspectBackupAsync(backupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task AutomaticBackup_UsesDeviceKeyEncryption()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "automatic.db");
        var backupDirectory = Path.Combine(directory, "automatic");
        const string password = "automatic-device-key";
        var keyProvider = new TestDatabaseKeyProvider(password);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString, keyProvider);
        var service = new BackupService(factory, databasePath, TimeProvider.System, keyProvider);
        try
        {
            await using (var db = factory.CreateDbContext())
                await db.Database.MigrateAsync();
            await service.CreateDailyAutomaticBackupAsync(backupDirectory);
            var backupPath = Assert.Single(Directory.GetFiles(backupDirectory, "automatic-*.hbak"));
            var summary = await service.InspectBackupAsync(backupPath, password);
            Assert.True(summary.IsEncrypted);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnencryptedBackup_IsClearlyIdentified()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "plain.hbak");
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
                await db.Database.MigrateAsync();
            await service.CreateBackupAsync(backupPath);
            var summary = await service.InspectBackupAsync(backupPath);
            Assert.False(summary.IsEncrypted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreWithWrongPassword_DoesNotChangeCurrentDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "manual.hbak");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                { Name = "Original", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }

            await service.CreateBackupAsync(backupPath, "correct horse battery");
            await using (var db = factory.CreateDbContext())
            {
                var account = await db.Accounts.SingleAsync();
                account.Name = "Current";
                await db.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.RestoreBackupAsync(backupPath, "incorrect password"));

            await using (var db = factory.CreateDbContext())
                Assert.Equal("Current", await db.Accounts.Select(x => x.Name).SingleAsync());
            Assert.Empty(Directory.GetFiles(directory, "pre-restore-*.hbak"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreBackup_WhenReplacementMoveFails_RestoresOriginalDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "rollback.hbak");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, ForeignKeys = true, Pooling = false
        }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var fileOperations = new FailingRestoreMoveOperations(databasePath);
        var service = new BackupService(factory, databasePath, TimeProvider.System,
            fileOperations: fileOperations);
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.MigrateAsync();
                db.Accounts.Add(new DbAccount
                { Name = "Original", Type = AccountType.Asset, Kind = AccountKind.Cash, CurrencyCode = "IRR" });
                await db.SaveChangesAsync();
            }

            await service.CreateBackupAsync(backupPath, "rollback password");
            await using (var db = factory.CreateDbContext())
            {
                var account = await db.Accounts.SingleAsync();
                account.Name = "Current";
                await db.SaveChangesAsync();
            }
            SqliteConnection.ClearAllPools();

            await Assert.ThrowsAsync<IOException>(() =>
                service.RestoreBackupAsync(backupPath, "rollback password"));

            await using var restored = factory.CreateDbContext();
            Assert.Equal("Current", await restored.Accounts.Select(x => x.Name).SingleAsync());
            Assert.Empty(Directory.GetFiles(directory, "*.rollback"));
            Assert.Empty(Directory.GetFiles(directory, "*.restore"));
            Assert.True(fileOperations.FailureInjected);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailingRestoreMoveOperations(string databasePath) : IBackupFileOperations
    {
        public bool FailureInjected { get; private set; }

        public void Move(string sourcePath, string destinationPath)
        {
            if (!FailureInjected &&
                sourcePath.EndsWith(".restore", StringComparison.Ordinal) &&
                string.Equals(destinationPath, databasePath, StringComparison.Ordinal))
            {
                FailureInjected = true;
                throw new IOException("Injected replacement installation failure.");
            }

            File.Move(sourcePath, destinationPath);
        }
    }

    [Fact]
    public async Task Backup_RestoresEncryptedAttachmentFilesAlongsideDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "hesabyar.db");
        var backupPath = Path.Combine(directory, "with-attachments.hbak");
        var attachmentsDirectory = Path.Combine(directory, "attachments");
        Directory.CreateDirectory(attachmentsDirectory);
        var storedName = Guid.NewGuid().ToString("N") + ".hatt";
        var originalBytes = RandomNumberGenerator.GetBytes(4096);
        var attachmentPath = Path.Combine(attachmentsDirectory, storedName);
        await File.WriteAllBytesAsync(attachmentPath, originalBytes);
        var connectionString = new SqliteConnectionStringBuilder
        { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
        var factory = new TestDbContextFactory(connectionString);
        var service = new BackupService(factory, databasePath, TimeProvider.System,
            attachmentsDirectory: attachmentsDirectory);
        try
        {
            await using (var db = factory.CreateDbContext())
                await db.Database.MigrateAsync();

            await service.CreateBackupAsync(backupPath, "attachment backup password");
            await File.WriteAllBytesAsync(attachmentPath, RandomNumberGenerator.GetBytes(1024));

            await service.RestoreBackupAsync(backupPath, "attachment backup password");

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(attachmentPath));
            var summary = await service.InspectBackupAsync(backupPath, "attachment backup password");
            Assert.True(summary.BackupFileSizeBytes > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PortableBackup_RekeysAttachmentsForTargetDevice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HesabyarBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "portable-with-attachment.hbak");
        var sourceAttachments = Path.Combine(directory, "source-attachments");
        var targetAttachments = Path.Combine(directory, "target-attachments");
        Directory.CreateDirectory(sourceAttachments);
        const string sourcePassword = "source-device-key";
        const string targetPassword = "target-device-key";
        const string backupPassword = "portable attachment backup password";
        var sourceKey = new TestDatabaseKeyProvider(sourcePassword);
        var targetKey = new TestDatabaseKeyProvider(targetPassword);
        var sourceFactory = new TestDbContextFactory(new SqliteConnectionStringBuilder
        { DataSource = sourcePath, ForeignKeys = true, Pooling = false }.ToString(), sourceKey);
        var targetFactory = new TestDbContextFactory(new SqliteConnectionStringBuilder
        { DataSource = targetPath, ForeignKeys = true, Pooling = false }.ToString(), targetKey);
        var sourceService = new BackupService(sourceFactory, sourcePath, TimeProvider.System, sourceKey,
            attachmentsDirectory: sourceAttachments);
        var targetService = new BackupService(targetFactory, targetPath, TimeProvider.System, targetKey,
            attachmentsDirectory: targetAttachments);
        var plain = "%PDF-1.7\\nportable receipt\\n%%EOF"u8.ToArray();
        var storedName = Guid.NewGuid().ToString("N") + ".hatt";
        try
        {
            await using (var db = sourceFactory.CreateDbContext()) await db.Database.MigrateAsync();
            await using (var db = targetFactory.CreateDbContext()) await db.Database.MigrateAsync();
            await File.WriteAllBytesAsync(Path.Combine(sourceAttachments, storedName),
                EncryptTestAttachment(plain, sourcePassword));

            await sourceService.CreateBackupAsync(backupPath, backupPassword);
            await targetService.RestoreBackupAsync(backupPath, backupPassword);

            var restoredPayload = await File.ReadAllBytesAsync(Path.Combine(targetAttachments, storedName));
            Assert.Equal(plain, DecryptTestAttachment(restoredPayload, targetPassword));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] EncryptTestAttachment(byte[] plaintext, string password)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        var key = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        try
        {
            var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(nonceLength);
            var tag = new byte[tagLength];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new System.Security.Cryptography.AesGcm(key, tagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            return [1, .. nonce, .. tag, .. ciphertext];
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DecryptTestAttachment(byte[] payload, string password)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        var key = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        var plaintext = new byte[payload.Length - 1 - nonceLength - tagLength];
        try
        {
            using var aes = new System.Security.Cryptography.AesGcm(key, tagLength);
            aes.Decrypt(payload.AsSpan(1, nonceLength), payload.AsSpan(1 + nonceLength + tagLength),
                payload.AsSpan(1 + nonceLength, tagLength), plaintext);
            return plaintext;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(key); }
    }

    private sealed class TestDbContextFactory(
        string connectionString, IDatabaseKeyProvider? keyProvider = null) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString);
            if (keyProvider is not null)
                optionsBuilder.AddInterceptors(new SqliteConnectionPragmaInterceptor(keyProvider));
            var options = optionsBuilder.Options;
            return new AppDbContext(options);
        }
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class TestDatabaseKeyProvider(string password) : IDatabaseKeyProvider
    {
        public string GetDatabasePassword() => password;
    }
}