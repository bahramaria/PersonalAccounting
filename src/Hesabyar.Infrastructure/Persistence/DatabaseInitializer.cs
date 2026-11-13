using Hesabyar.Application.Common;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IDbContextFactory<AppDbContext> contextFactory,
    IClock clock)
{
    private static readonly (string Key, string Name, AccountType Type, int SortOrder)[] DefaultCategories =
    [
        ("seed:expense:food", "خوراک", AccountType.Expense, 10),
        ("seed:expense:housing", "مسکن و اجاره", AccountType.Expense, 20),
        ("seed:expense:transport", "حمل‌ونقل", AccountType.Expense, 30),
        ("seed:expense:health", "سلامت", AccountType.Expense, 40),
        ("seed:expense:education", "آموزش", AccountType.Expense, 50),
        ("seed:expense:leisure", "تفریح", AccountType.Expense, 60),
        ("seed:expense:bills", "قبوض", AccountType.Expense, 70),
        ("seed:expense:installments", "اقساط", AccountType.Expense, 80),
        ("seed:expense:clothing", "پوشاک", AccountType.Expense, 90),
        ("seed:expense:gifts", "مناسبت و هدیه", AccountType.Expense, 100),
        ("seed:expense:other", "سایر هزینه‌ها", AccountType.Expense, 110),
        ("seed:income:salary", "حقوق", AccountType.Income, 10),
        ("seed:income:other", "سایر درآمدها", AccountType.Income, 20)
    ];

    public async Task InitializeAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingDatabasePath = GetExistingDatabasePath(db.Database.GetConnectionString());
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pendingMigrations.Length > 0)
        {
            if (existingDatabasePath is not null)
                await BackupExistingDatabaseAsync(db.Database.GetConnectionString()!, backupDirectory, cancellationToken);

            await db.Database.MigrateAsync(cancellationToken);
        }

        await SeedDefaultCategoriesAsync(db, cancellationToken);
    }

    private async Task SeedDefaultCategoriesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingKeys = await db.Accounts
            .IgnoreQueryFilters()
            .Where(x => x.SystemKey != null && x.SystemKey.StartsWith("seed:"))
            .Select(x => x.SystemKey!)
            .ToHashSetAsync(cancellationToken);

        var nowUtc = clock.UtcNow;
        foreach (var category in DefaultCategories)
        {
            if (existingKeys.Contains(category.Key))
                continue;

            db.Accounts.Add(new DbAccount
            {
                Name = category.Name,
                Type = category.Type,
                Kind = AccountKind.Category,
                CurrencyCode = "IRR",
                SystemKey = category.Key,
                SortOrder = category.SortOrder,
                IncludeInTotal = false,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(cancellationToken);
    }

    private static string? GetExistingDatabasePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var connection = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = connection.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource) ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
            connection.Mode == SqliteOpenMode.Memory)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(dataSource);
        return File.Exists(fullPath) ? fullPath : null;
    }

    private async Task BackupExistingDatabaseAsync(
        string connectionString,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupDirectory);
        var timestamp = clock.UtcNow.ToString(
            "yyyyMMddTHHmmssfffZ",
            System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(
            backupDirectory,
            $"pre-migration-{timestamp}-{Guid.CreateVersion7():N}.db");

        await using var source = new SqliteConnection(connectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }
}
