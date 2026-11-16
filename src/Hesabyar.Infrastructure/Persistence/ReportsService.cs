using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Localization.Calendar;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed record ReportCategoryRow(string Name, long AmountMinor);
public sealed record ReportMonthRow(DateOnly MonthStart, string Label, long IncomeMinor, long ExpenseMinor);
public sealed record ReportAccountRow(string Name, string Type, long BalanceMinor);
public sealed record ReportPersonRow(string Name, long BalanceMinor);

public sealed record FinancialReportSnapshot(
    DateOnly FromDate,
    DateOnly ToDate,
    long IncomeMinor,
    long ExpenseMinor,
    long AssetsMinor,
    long LiabilitiesMinor,
    long NetWorthMinor,
    IReadOnlyList<ReportCategoryRow> ExpensesByCategory,
    IReadOnlyList<ReportMonthRow> MonthlyTrend,
    IReadOnlyList<ReportAccountRow> Accounts,
    IReadOnlyList<ReportPersonRow> People);

public sealed class ReportsService(
    IDbContextFactory<AppDbContext> contextFactory,
    ICalendarService calendar)
{
    public async Task<FinancialReportSnapshot> GetAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
            throw new ArgumentException("The report start date must not be after its end date.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var transactions = await db.Transactions.AsNoTracking()
            .Where(x => x.IsConfirmed &&
                (x.Kind == TransactionKind.Expense || x.Kind == TransactionKind.Income) &&
                x.BusinessDate >= fromDate && x.BusinessDate <= toDate)
            .Include(x => x.Postings)
            .ThenInclude(x => x.Account)
            .ToListAsync(cancellationToken);

        var expensePostings = transactions
            .Where(x => x.Kind == TransactionKind.Expense)
            .SelectMany(x => x.Postings)
            .Where(x => x.Account.Type == AccountType.Expense && x.AmountMinor > 0)
            .ToArray();
        var incomePostings = transactions
            .Where(x => x.Kind == TransactionKind.Income)
            .SelectMany(x => x.Postings)
            .Where(x => x.Account.Type == AccountType.Income && x.AmountMinor < 0)
            .ToArray();

        var expenses = expensePostings.Sum(x => (decimal)x.AmountMinor);
        var income = incomePostings.Sum(x => Math.Abs((decimal)x.AmountMinor));

        var expensesByCategory = expensePostings
            .GroupBy(x => x.Account.Name)
            .Select(x => new ReportCategoryRow(x.Key, decimal.ToInt64(x.Sum(p => (decimal)p.AmountMinor))))
            .OrderByDescending(x => x.AmountMinor)
            .ThenBy(x => x.Name)
            .ToArray();

        var monthly = BuildMonthlyRows(fromDate, toDate, transactions);

        var accounts = await db.Accounts.AsNoTracking()
            .Where(x => !x.IsArchived && x.Kind != AccountKind.System)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var accountIds = accounts.Select(x => x.Id).ToArray();
        var accountBalances = accountIds.Length == 0
            ? new Dictionary<Guid, long>()
            : await db.Postings.AsNoTracking()
                .Where(x => accountIds.Contains(x.AccountId) && !x.Transaction.IsDeleted && x.Transaction.IsConfirmed)
                .GroupBy(x => x.AccountId)
                .Select(x => new { AccountId = x.Key, Balance = x.Sum(p => p.AmountMinor) })
                .ToDictionaryAsync(x => x.AccountId, x => x.Balance, cancellationToken);

        var accountRows = accounts.Select(x => new ReportAccountRow(
            x.Name, x.Type.ToString(), accountBalances.GetValueOrDefault(x.Id)))
            .ToArray();

        var accountAssets = accounts.Where(x => x.IncludeInTotal && x.Type == AccountType.Asset)
            .Sum(x => (decimal)accountBalances.GetValueOrDefault(x.Id));
        var accountLiabilities = accounts.Where(x => x.IncludeInTotal && x.Type == AccountType.Liability)
            .Sum(x => Math.Abs((decimal)accountBalances.GetValueOrDefault(x.Id)));

        var parties = await db.Parties.AsNoTracking()
            .Where(x => !x.IsArchived)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var partyIds = parties.Select(x => x.Id).ToArray();
        var partyBalances = partyIds.Length == 0
            ? new Dictionary<Guid, long>()
            : await db.Postings.AsNoTracking()
                .Where(x => x.PartyId != null && partyIds.Contains(x.PartyId.Value) &&
                    x.Account.SystemKey == "people-clearing" && !x.Transaction.IsDeleted && x.Transaction.IsConfirmed)
                .GroupBy(x => x.PartyId!.Value)
                .Select(x => new { PartyId = x.Key, Balance = x.Sum(p => p.AmountMinor) })
                .ToDictionaryAsync(x => x.PartyId, x => x.Balance, cancellationToken);

        var peopleRows = parties.Select(x => new ReportPersonRow(
            x.Name, partyBalances.GetValueOrDefault(x.Id))).ToArray();
        var peopleAssets = peopleRows.Where(x => x.BalanceMinor > 0).Sum(x => (decimal)x.BalanceMinor);
        var peopleLiabilities = peopleRows.Where(x => x.BalanceMinor < 0).Sum(x => Math.Abs((decimal)x.BalanceMinor));

        var loans = await db.Loans.AsNoTracking().Where(x => !x.IsClosed)
            .Include(x => x.Installments)
            .ToListAsync(cancellationToken);
        var loanAssets = loans.Where(x => x.Direction == Hesabyar.Application.Loans.LoanDirection.Given)
            .Sum(x => x.Installments.Sum(i => (decimal)(i.PrincipalDueMinor - i.PaidPrincipalMinor)));
        var loanLiabilities = loans.Where(x => x.Direction == Hesabyar.Application.Loans.LoanDirection.Received)
            .Sum(x => x.Installments.Sum(i =>
                (decimal)(i.PrincipalDueMinor - i.PaidPrincipalMinor) + i.InterestDueMinor - i.PaidInterestMinor));

        var assets = accountAssets + peopleAssets + loanAssets;
        var liabilities = accountLiabilities + peopleLiabilities + loanLiabilities;

        return new FinancialReportSnapshot(
            fromDate,
            toDate,
            decimal.ToInt64(income),
            decimal.ToInt64(expenses),
            decimal.ToInt64(assets),
            decimal.ToInt64(liabilities),
            decimal.ToInt64(assets - liabilities),
            expensesByCategory,
            monthly,
            accountRows,
            peopleRows);
    }

    private IReadOnlyList<ReportMonthRow> BuildMonthlyRows(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyCollection<DbLedgerTransaction> transactions)
    {
        var first = calendar.ToPersian(fromDate);
        var last = calendar.ToPersian(toDate);
        var month = new PersianDate(first.Year, first.Month, 1);
        var result = new List<ReportMonthRow>();

        while (month.Year < last.Year || month.Year == last.Year && month.Month <= last.Month)
        {
            var start = calendar.ToGregorian(month);
            var next = calendar.AddMonths(month, 1);
            var end = calendar.ToGregorian(next).AddDays(-1);
            var monthTransactions = transactions.Where(x => x.BusinessDate >= start && x.BusinessDate <= end);
            var monthIncome = monthTransactions.Where(x => x.Kind == TransactionKind.Income)
                .SelectMany(x => x.Postings)
                .Where(x => x.Account.Type == AccountType.Income && x.AmountMinor < 0)
                .Sum(x => Math.Abs((decimal)x.AmountMinor));
            var monthExpense = monthTransactions.Where(x => x.Kind == TransactionKind.Expense)
                .SelectMany(x => x.Postings)
                .Where(x => x.Account.Type == AccountType.Expense && x.AmountMinor > 0)
                .Sum(x => (decimal)x.AmountMinor);
            result.Add(new ReportMonthRow(start, $"{month.Year:0000}/{month.Month:00}",
                decimal.ToInt64(monthIncome), decimal.ToInt64(monthExpense)));
            month = next;
        }

        return result;
    }
}
