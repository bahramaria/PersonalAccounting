using Hesabyar.Application.Accounts;
using Hesabyar.Application.Common;
using Hesabyar.Application.Loans;
using Hesabyar.Domain.Accounts;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hesabyar.Infrastructure.Tests;

public sealed class LoanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 1, 31);


    [Fact]
    public void ModelSnapshot_MatchesCurrentEfModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(options);
        var snapshot = new AppDbContextModelSnapshot();
        var initializedSnapshot = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var currentDesignTimeModel = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            initializedSnapshot.GetRelationalModel(), currentDesignTimeModel.GetRelationalModel());

        Assert.True(differences.Count == 0,
            string.Join(Environment.NewLine, differences.Select(x => x.GetType().Name + ": " + x)));
    }

    [Theory]
    [InlineData(1_000_000L, 0, 1)]
    [InlineData(1_000_000L, 0, 3)]
    [InlineData(1_000_000L, 1200, 6)]
    [InlineData(1_000_000L, 1800, 12)]
    [InlineData(1_000_000L, 2400, 24)]
    [InlineData(5_000_000L, 3600, 36)]
    [InlineData(750_000L, 500, 5)]
    [InlineData(2_500_000L, 1500, 18)]
    [InlineData(9_999_999L, 3000, 48)]
    [InlineData(123_456L, 100, 7)]
    public async Task AnnuitySchedule_PreservesPrincipalAndProducesValidDates(long principal, int rate, int count)
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Reference", LoanDirection.Given, account, null, principal,
            Start, LoanScheduleType.Annuity, count, rate, null, null);

        var result = await fixture.Service.CreateAsync(request);
        var loan = Assert.Single(await fixture.Service.GetLoansAsync());

        Assert.True(result.IsSuccess);
        Assert.Equal(count, loan.Installments.Count);
        Assert.Equal(principal, loan.Installments.Sum(x => x.PrincipalDueMinor));
        Assert.All(loan.Installments, x =>
        {
            Assert.True(x.PrincipalDueMinor > 0);
            Assert.True(x.InterestDueMinor >= 0);
            Assert.True(x.DueDate > Start);
        });
        Assert.Equal(Start.AddMonths(1), loan.Installments[0].DueDate);
    }


    [Fact]
    public async Task Annuity_UsesMonthlyReducingBalanceInterestAndKnownReferenceAmounts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Reference 12 percent", LoanDirection.Received, account, null, 1_200_000,
            new DateOnly(2026, 1, 31), LoanScheduleType.Annuity, 12, 1200, null, null);

        var result = await fixture.Service.CreateAsync(request);
        var loan = Assert.Single(await fixture.Service.GetLoansAsync());

        Assert.True(result.IsSuccess);
        Assert.Equal(12_000, loan.Installments[0].InterestDueMinor);
        Assert.Equal(94_619, loan.Installments[0].PrincipalDueMinor);
        Assert.Equal(79_423, loan.Installments.Sum(x => x.InterestDueMinor));
        Assert.Equal(new DateOnly(2026, 2, 28), loan.Installments[0].DueDate);
        Assert.Equal(new DateOnly(2026, 3, 31), loan.Installments[1].DueDate);
    }

    [Fact]
    public async Task ManualSchedule_RejectsPrincipalMismatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Manual", LoanDirection.Received, account, null, 1_000_000,
            Start, LoanScheduleType.Manual, 2, 0,
            [new ManualInstallmentRequest(Start.AddMonths(1), 400_000, 10_000),
             new ManualInstallmentRequest(Start.AddMonths(2), 400_000, 10_000)], null);

        var result = await fixture.Service.CreateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Service.GetLoansAsync());
    }

    [Fact]
    public async Task ManualSchedule_ClampsEndOfMonthAndPartialPaymentAllocatesInterestFirst()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Manual", LoanDirection.Given, account, null, 1_000_000,
            Start, LoanScheduleType.Manual, 2, 0,
            [new ManualInstallmentRequest(new DateOnly(2026, 2, 28), 500_000, 50_000),
             new ManualInstallmentRequest(new DateOnly(2026, 3, 31), 500_000, 50_000)], null);
        var created = await fixture.Service.CreateAsync(request);
        Assert.True(created.IsSuccess);

        var paid = await fixture.Service.PayAsync(created.Value, account, 200_000, new DateOnly(2026, 2, 28));
        var loan = Assert.Single(await fixture.Service.GetLoansAsync());

        Assert.True(paid.IsSuccess);
        Assert.Equal(900_000, loan.RemainingMinor);
        Assert.Equal(350_000, loan.Installments[0].RemainingPrincipalMinor);
        Assert.Equal(0, loan.Installments[0].RemainingInterestMinor);
        Assert.Equal(0L, await fixture.Db.Transactions.Where(x => x.LoanId == created.Value)
            .SelectMany(x => x.Postings).SumAsync(x => x.AmountMinor));
    }

    [Theory]
    [InlineData(LoanDirection.Given)]
    [InlineData(LoanDirection.Received)]
    public async Task LoanOpeningAndPartialPayment_CreateBalancedLedgerEntries(LoanDirection direction)
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Test", direction, account, null, 1_000_000,
            Start, LoanScheduleType.Manual, 1, 0,
            [new ManualInstallmentRequest(Start.AddMonths(1), 1_000_000, 100_000)], null);
        var created = await fixture.Service.CreateAsync(request);
        Assert.True(created.IsSuccess);

        var paid = await fixture.Service.PayAsync(created.Value, account, 250_000, Start.AddMonths(1));
        var loan = Assert.Single(await fixture.Service.GetLoansAsync());

        Assert.True(paid.IsSuccess);
        Assert.Equal(850_000, loan.RemainingMinor);
        Assert.Equal(850_000, loan.RemainingPrincipalMinor + loan.RemainingInterestMinor);
        var transactions = await fixture.Db.Transactions.Where(x => x.LoanId == created.Value)
            .Include(x => x.Postings).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, x => Assert.Equal(0L, x.Postings.Sum(p => p.AmountMinor)));
    }

    [Fact]
    public async Task PaymentCannotExceedOutstandingAndFinalPaymentClosesLoan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.CreateAccountAsync();
        var request = new CreateLoanRequest("Close", LoanDirection.Received, account, null, 100_000,
            Start, LoanScheduleType.Manual, 1, 0,
            [new ManualInstallmentRequest(Start.AddMonths(1), 100_000, 0)], null);
        var created = await fixture.Service.CreateAsync(request);
        Assert.True(created.IsSuccess);

        Assert.True((await fixture.Service.PayAsync(created.Value, account, 100_001, Start.AddMonths(1))).IsFailure);
        Assert.True((await fixture.Service.PayAsync(created.Value, account, 100_000, Start.AddMonths(1))).IsSuccess);
        var loan = Assert.Single(await fixture.Service.GetLoansAsync());
        Assert.Equal(0, loan.RemainingMinor);
        Assert.True(loan.IsClosed);
        Assert.Null(loan.NextInstallment);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public LoanService Service { get; }

        private Fixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new LoanService(new LoanRepository(db), new FixedClock());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            return new Fixture(connection, db);
        }

        public async Task<Guid> CreateAccountAsync()
        {
            var service = new AccountService(new EfAccountingRepository(Db), new FixedClock());
            var result = await service.CreateAsync(new CreateAccountRequest("Bank", AccountType.Asset, AccountKind.Bank));
            Assert.True(result.IsSuccess);
            return result.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
