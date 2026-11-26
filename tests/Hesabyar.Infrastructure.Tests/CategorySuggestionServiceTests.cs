using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Tests;

public sealed class CategorySuggestionServiceTests
{
    [Fact]
    public async Task Suggestion_PrefersSpecificPhrase_AndTracksOnlyAcceptedUse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var transport = await fixture.AddAccountAsync("Transport", AccountType.Expense, AccountKind.Category);
        var food = await fixture.AddAccountAsync("Food", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        await service.LearnAsync("snapp", TransactionKind.Expense, transport, account);
        await service.LearnAsync("snapp food", TransactionKind.Expense, food, account);

        var suggestion = await service.SuggestAsync("خرید SNAPP FOOD", TransactionKind.Expense, account, "IRR");

        Assert.NotNull(suggestion);
        Assert.Equal(food, suggestion.CategoryId);
        Assert.Contains("snapp food", suggestion.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, suggestion.UsageCount);

        Assert.True(await service.RecordAcceptedSuggestionAsync(suggestion.RuleId));
        var rule = Assert.Single(await service.GetRulesAsync(), x => x.MatchText == "snapp food");
        Assert.Equal(1, rule.UsageCount);
    }

    [Fact]
    public async Task Suggestion_PrefersAccountSpecificRuleOverLongerGlobalRule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var globalCategory = await fixture.AddAccountAsync("Food", AccountType.Expense, AccountKind.Category);
        var specificCategory = await fixture.AddAccountAsync("Transport", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        await service.LearnAsync("snapp food", TransactionKind.Expense, globalCategory);
        await service.LearnAsync("snapp", TransactionKind.Expense, specificCategory, account);

        var suggestion = await service.SuggestAsync("snapp food", TransactionKind.Expense, account, "IRR");

        Assert.NotNull(suggestion);
        Assert.Equal(specificCategory, suggestion.CategoryId);
    }

    [Fact]
    public async Task Suggestion_ReturnsNullWhenBestMatchesConflict()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var first = await fixture.AddAccountAsync("Cafe", AccountType.Expense, AccountKind.Category);
        var second = await fixture.AddAccountAsync("Shopping", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        await service.LearnAsync("cafe 1", TransactionKind.Expense, first, account);
        await service.LearnAsync("cafe 2", TransactionKind.Expense, second, account);

        var suggestion = await service.SuggestAsync("cafe 1 cafe 2", TransactionKind.Expense, account, "IRR");

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task DisabledRulesAreIgnored_AndWrongCategoryTypeIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var expense = await fixture.AddAccountAsync("Expense", AccountType.Expense, AccountKind.Category);
        var income = await fixture.AddAccountAsync("Income", AccountType.Income, AccountKind.Category);
        var service = fixture.CreateService();

        var ruleId = await service.LearnAsync("grocery store", TransactionKind.Expense, expense, account);
        Assert.True(await service.SetEnabledAsync(ruleId, false));
        Assert.Null(await service.SuggestAsync("grocery store", TransactionKind.Expense, account, "IRR"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.LearnAsync("wrong type", TransactionKind.Expense, income, account));
    }

    [Fact]
    public async Task Rules_CanBeEditedDisabledAndDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var first = await fixture.AddAccountAsync("Food", AccountType.Expense, AccountKind.Category);
        var second = await fixture.AddAccountAsync("Transport", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        var id = await service.LearnAsync("old phrase", TransactionKind.Expense, first, account);
        Assert.True(await service.UpdateRuleAsync(id, "new phrase", second));
        var updated = Assert.Single(await service.GetRulesAsync());
        Assert.Equal("new phrase", updated.MatchText);
        Assert.Equal(second, updated.CategoryId);

        Assert.True(await service.SetEnabledAsync(id, false));
        Assert.False(Assert.Single(await service.GetRulesAsync()).IsEnabled);
        Assert.True(await service.DeleteAsync(id));
        Assert.Empty(await service.GetRulesAsync());
    }

    [Fact]
    public async Task Suggestion_RequiresWholePhraseAndIgnoresArchivedCategories()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var category = await fixture.AddAccountAsync("Transport", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        await service.LearnAsync("car", TransactionKind.Expense, category, account);
        Assert.Null(await service.SuggestAsync("carpet purchase", TransactionKind.Expense, account, "IRR"));

        await fixture.ArchiveAccountAsync(category);
        Assert.Null(await service.SuggestAsync("car repair", TransactionKind.Expense, account, "IRR"));
    }

    [Fact]
    public async Task DeleteAll_RemovesEveryRule()
    {
        await using var fixture = await Fixture.CreateAsync();
        var account = await fixture.AddAccountAsync("Bank", AccountType.Asset, AccountKind.Bank);
        var category = await fixture.AddAccountAsync("Food", AccountType.Expense, AccountKind.Category);
        var service = fixture.CreateService();

        await service.LearnAsync("market", TransactionKind.Expense, category, account);
        await service.LearnAsync("grocery", TransactionKind.Expense, category);

        Assert.Equal(2, await service.DeleteAllAsync());
        Assert.Empty(await service.GetRulesAsync());
    }

    [Fact]
    public void NormalizeMatchText_NormalizesPersianArabicAndWhitespace()
    {
        Assert.Equal("کافه یاس 123", CategorySuggestionService.NormalizeMatchText("  كافه‌ياس ۱۲۳ "));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        private Fixture(SqliteConnection connection, DbContextOptions<AppDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            await using (var db = new AppDbContext(options))
                await db.Database.MigrateAsync();
            return new Fixture(connection, options);
        }

        public async Task<Guid> AddAccountAsync(string name, AccountType type, AccountKind kind)
        {
            await using var db = new AppDbContext(_options);
            var account = new DbAccount
            {
                Name = name,
                Type = type,
                Kind = kind,
                CurrencyCode = "IRR"
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
            return account.Id;
        }

        public async Task ArchiveAccountAsync(Guid accountId)
        {
            await using var db = new AppDbContext(_options);
            var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
            account.IsArchived = true;
            await db.SaveChangesAsync();
        }

        public CategorySuggestionService CreateService() =>
            new(new SharedFactory(_options), TimeProvider.System);

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
        }

        private sealed class SharedFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
        {
            public AppDbContext CreateDbContext() => new(options);
            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(new AppDbContext(options));
        }
    }
}
