using Hesabyar.Application.Cheques;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class ChequeRepositoryTests
{
    [Fact]
    public async Task ReceivedCheque_ClearAndReturn_PostsAndReversesBankMovement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        var bank = new DbAccount
        {
            Name = "Bank", Type = AccountType.Asset, Kind = AccountKind.Bank, CurrencyCode = "IRR"
        };
        db.Accounts.Add(bank);
        await db.SaveChangesAsync();

        var repository = new ChequesRepository(db);
        var id = await repository.CreateAsync(new CreateChequeRequest(
            ChequeDirection.Received, "Melli", "12345", "1234567890123456",
            new DateOnly(2026, 12, 1), null, 750_000, "Rent"), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(id);
        Assert.True(await repository.ChangeStatusAsync(id.Value, ChequeStatus.Cleared, bank.Id,
            new DateOnly(2026, 12, 1), DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(750_000, await db.Postings.Where(x => x.AccountId == bank.Id).SumAsync(x => x.AmountMinor));

        Assert.True(await repository.ChangeStatusAsync(id.Value, ChequeStatus.Returned, null,
            new DateOnly(2026, 12, 2), DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None));
        Assert.Equal(0L, await db.Postings.Where(x => x.AccountId == bank.Id).SumAsync(x => x.AmountMinor));
        Assert.Equal(ChequeStatus.Returned, (await repository.GetAsync(true, CancellationToken.None)).Single().Status);
        Assert.Equal(2, await db.Transactions.CountAsync(x => x.Kind == TransactionKind.ChequeEvent));
    }

    [Fact]
    public async Task DuplicateBankSerialOrSayadId_IsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var repository = new ChequesRepository(db);
        var first = new CreateChequeRequest(ChequeDirection.Issued, "Melli", "A-1", "1111111111111111",
            new DateOnly(2026, 12, 1), null, 100, null);
        Assert.NotNull(await repository.CreateAsync(first, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Null(await repository.CreateAsync(first with { SerialNumber = "A-2" },
            DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Null(await repository.CreateAsync(first with { SayadNumber = null },
            DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
