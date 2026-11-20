using System.Globalization;
using System.Text;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed record CategorySuggestion(
    Guid RuleId,
    Guid CategoryId,
    string CategoryName,
    string MatchText,
    double Confidence,
    string Reason,
    int UsageCount);

public sealed record CategorySuggestionRuleSnapshot(
    Guid Id,
    string MatchText,
    TransactionKind TransactionKind,
    Guid CategoryId,
    string CategoryName,
    string CurrencyCode,
    Guid? AccountId,
    string? AccountName,
    bool IsEnabled,
    int UsageCount);

public sealed record CategorySuggestionCategory(Guid Id, string Name, string CurrencyCode);

public sealed class CategorySuggestionService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider)
{
    public async Task<CategorySuggestion?> SuggestAsync(
        string? description,
        TransactionKind transactionKind,
        Guid accountId,
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        if (transactionKind is not (TransactionKind.Expense or TransactionKind.Income) ||
            string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(currencyCode))
            return null;

        var normalized = NormalizeMatchText(description);
        if (normalized.Length < 3)
            return null;

        var expectedCategoryType = transactionKind == TransactionKind.Income
            ? AccountType.Income
            : AccountType.Expense;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await (
            from rule in db.CategorySuggestionRules.AsNoTracking()
            join category in db.Accounts.AsNoTracking() on rule.CategoryId equals category.Id
            where rule.IsEnabled &&
                  rule.TransactionKind == transactionKind &&
                  category.Kind == AccountKind.Category &&
                  category.Type == expectedCategoryType &&
                  category.CurrencyCode == currencyCode &&
                  (rule.AccountId == null || rule.AccountId == accountId)
            select new
            {
                Rule = rule,
                CategoryName = category.Name,
                AccountSpecific = rule.AccountId != null
            }).ToListAsync(cancellationToken);

        var matches = candidates
            .Where(x => normalized.Contains(x.Rule.NormalizedMatchText, StringComparison.Ordinal))
            .OrderByDescending(x => x.Rule.NormalizedMatchText.Length)
            .ThenByDescending(x => x.AccountSpecific)
            .ThenByDescending(x => x.Rule.UsageCount)
            .ToArray();
        if (matches.Length == 0)
            return null;

        var bestLength = matches[0].Rule.NormalizedMatchText.Length;
        var bestAccountSpecificity = matches[0].AccountSpecific;
        var best = matches.Where(x =>
            x.Rule.NormalizedMatchText.Length == bestLength &&
            x.AccountSpecific == bestAccountSpecific).ToArray();
        if (best.Select(x => x.Rule.CategoryId).Distinct().Count() != 1)
            return null;

        var selected = best[0];
        var exact = normalized == selected.Rule.NormalizedMatchText;
        var confidence = exact ? 0.95 : Math.Min(0.9, 0.78 + selected.Rule.NormalizedMatchText.Length / 100d);
        var scope = selected.AccountSpecific ? "for this account" : "for all accounts";
        var reason = $"Description matches '{selected.Rule.MatchText}' ({scope}).";

        return new CategorySuggestion(
            selected.Rule.Id,
            selected.Rule.CategoryId,
            selected.CategoryName,
            selected.Rule.MatchText,
            confidence,
            reason,
            selected.Rule.UsageCount);
    }

    public async Task<Guid> LearnAsync(
        string matchText,
        TransactionKind transactionKind,
        Guid categoryId,
        Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        if (transactionKind is not (TransactionKind.Expense or TransactionKind.Income))
            throw new ArgumentOutOfRangeException(nameof(transactionKind));

        var normalized = NormalizeMatchText(matchText);
        if (normalized.Length is < 3 or > 120)
            throw new ArgumentException("A rule phrase must contain between 3 and 120 normalized characters.", nameof(matchText));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == categoryId && !x.IsArchived &&
                 x.Kind == AccountKind.Category &&
                 x.Type == (transactionKind == TransactionKind.Income ? AccountType.Income : AccountType.Expense),
            cancellationToken);
        if (category is null)
            throw new ArgumentException("The selected category is invalid for this transaction type.", nameof(categoryId));

        if (accountId is Guid selectedAccountId)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == selectedAccountId && !x.IsArchived &&
                     x.Kind != AccountKind.Category && x.CurrencyCode == category.CurrencyCode,
                cancellationToken);
            if (account is null)
                throw new ArgumentException("The selected account is invalid or uses a different currency.", nameof(accountId));
        }

        var rule = await db.CategorySuggestionRules.SingleOrDefaultAsync(
            x => x.NormalizedMatchText == normalized &&
                 x.TransactionKind == transactionKind &&
                 x.AccountId == accountId,
            cancellationToken);
        if (rule is null)
        {
            rule = new DbCategorySuggestionRule
            {
                MatchText = matchText.Trim(),
                NormalizedMatchText = normalized,
                TransactionKind = transactionKind,
                CategoryId = categoryId,
                AccountId = accountId,
                IsEnabled = true
            };
            db.CategorySuggestionRules.Add(rule);
        }
        else
        {
            rule.MatchText = matchText.Trim();
            rule.CategoryId = categoryId;
            rule.IsEnabled = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return rule.Id;
    }

    public async Task<bool> RecordAcceptedSuggestionAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.CategorySuggestionRules.SingleOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null || !rule.IsEnabled)
            return false;
        rule.UsageCount = checked(rule.UsageCount + 1);
        rule.LastUsedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CategorySuggestionRuleSnapshot>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db.CategorySuggestionRules.AsNoTracking()
            .OrderBy(x => x.TransactionKind)
            .ThenBy(x => x.MatchText)
            .ToListAsync(cancellationToken);
        var categoryIds = rules.Select(x => x.CategoryId).Distinct().ToArray();
        var accountIds = rules.Where(x => x.AccountId.HasValue).Select(x => x.AccountId!.Value).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id) || accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return rules.Select(rule =>
        {
            accounts.TryGetValue(rule.CategoryId, out var category);
            accounts.TryGetValue(rule.AccountId ?? Guid.Empty, out var account);
            return new CategorySuggestionRuleSnapshot(
                rule.Id, rule.MatchText, rule.TransactionKind, rule.CategoryId,
                category?.Name ?? "(missing category)", category?.CurrencyCode ?? "",
                rule.AccountId, account?.Name, rule.IsEnabled, rule.UsageCount);
        }).ToArray();
    }

    public async Task<IReadOnlyList<CategorySuggestionCategory>> GetCategoriesAsync(
        TransactionKind transactionKind,
        CancellationToken cancellationToken = default)
    {
        if (transactionKind is not (TransactionKind.Expense or TransactionKind.Income))
            return [];

        var expectedType = transactionKind == TransactionKind.Income ? AccountType.Income : AccountType.Expense;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Accounts.AsNoTracking()
            .Where(x => !x.IsArchived && x.Kind == AccountKind.Category && x.Type == expectedType)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new CategorySuggestionCategory(x.Id, x.Name, x.CurrencyCode))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> UpdateRuleAsync(
        Guid ruleId,
        string matchText,
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeMatchText(matchText);
        if (normalized.Length is < 3 or > 120)
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.CategorySuggestionRules.SingleOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;

        var category = await db.Accounts.SingleOrDefaultAsync(
            x => x.Id == categoryId && !x.IsArchived &&
                 x.Kind == AccountKind.Category &&
                 x.Type == (rule.TransactionKind == TransactionKind.Income ? AccountType.Income : AccountType.Expense),
            cancellationToken);
        if (category is null)
            return false;

        if (rule.AccountId is Guid accountId)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == accountId && !x.IsArchived && x.CurrencyCode == category.CurrencyCode,
                cancellationToken);
            if (account is null)
                return false;
        }

        rule.MatchText = matchText.Trim();
        rule.NormalizedMatchText = normalized;
        rule.CategoryId = categoryId;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetEnabledAsync(Guid ruleId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.CategorySuggestionRules.SingleOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;
        rule.IsEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.CategorySuggestionRules.SingleOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;
        rule.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static string NormalizeMatchText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace('\u200c', ' ');
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            var digit = character switch
            {
                >= '۰' and <= '۹' => (char)('0' + character - '۰'),
                >= '٠' and <= '٩' => (char)('0' + character - '٠'),
                _ => character
            };
            if (char.IsLetterOrDigit(digit))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(char.ToLower(digit, CultureInfo.InvariantCulture));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString().Trim();
    }
}
