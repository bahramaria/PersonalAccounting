using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed record ReportCategoryRow(string Name, long AmountMinor);
public sealed record ReportMonthRow(DateOnly MonthStart, string Label, long IncomeMinor, long ExpenseMinor);
public sealed record ReportAccountRow(string Name, string Type, long BalanceMinor);
public sealed record ReportPersonRow(string Name, long BalanceMinor, long PeriodChangeMinor, int ActivityCount);

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

public sealed class ReportsService(IDbContextFactory<AppDbContext> contextFactory)
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
            .GroupBy(x => new { x.AccountId, x.Account.Name })
            .Select(x => new ReportCategoryRow(x.Key.Name, decimal.ToInt64(x.Sum(p => (decimal)p.AmountMinor))))
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

        var periodPartyBalances = partyIds.Length == 0
            ? new Dictionary<Guid, (long Balance, int Count)>()
            : await db.Postings.AsNoTracking()
                .Where(x => x.PartyId != null && partyIds.Contains(x.PartyId.Value) &&
                    x.Account.SystemKey == "people-clearing" && !x.Transaction.IsDeleted &&
                    x.Transaction.IsConfirmed && x.Transaction.BusinessDate >= fromDate &&
                    x.Transaction.BusinessDate <= toDate)
                .GroupBy(x => x.PartyId!.Value)
                .Select(x => new { PartyId = x.Key, Balance = x.Sum(p => p.AmountMinor), Count = x.Count() })
                .ToDictionaryAsync(x => x.PartyId, x => (x.Balance, x.Count), cancellationToken);

        var peopleRows = parties.Select(x =>
        {
            var period = periodPartyBalances.GetValueOrDefault(x.Id);
            return new ReportPersonRow(x.Name, partyBalances.GetValueOrDefault(x.Id),
                period.Balance, period.Count);
        }).ToArray();
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
        var persianCalendar = new System.Globalization.PersianCalendar();
        var fromValue = fromDate.ToDateTime(TimeOnly.MinValue);
        var toValue = toDate.ToDateTime(TimeOnly.MinValue);
        var year = persianCalendar.GetYear(fromValue);
        var monthNumber = persianCalendar.GetMonth(fromValue);
        var lastYear = persianCalendar.GetYear(toValue);
        var lastMonth = persianCalendar.GetMonth(toValue);
        var result = new List<ReportMonthRow>();

        while (year < lastYear || year == lastYear && monthNumber <= lastMonth)
        {
            var start = DateOnly.FromDateTime(persianCalendar.ToDateTime(year, monthNumber, 1, 0, 0, 0, 0));
            var nextYear = monthNumber == 12 ? year + 1 : year;
            var nextMonth = monthNumber == 12 ? 1 : monthNumber + 1;
            var end = DateOnly.FromDateTime(persianCalendar.ToDateTime(nextYear, nextMonth, 1, 0, 0, 0, 0)).AddDays(-1);
            var monthTransactions = transactions.Where(x => x.BusinessDate >= start && x.BusinessDate <= end);
            var monthIncome = monthTransactions.Where(x => x.Kind == TransactionKind.Income)
                .SelectMany(x => x.Postings)
                .Where(x => x.Account.Type == AccountType.Income && x.AmountMinor < 0)
                .Sum(x => Math.Abs((decimal)x.AmountMinor));
            var monthExpense = monthTransactions.Where(x => x.Kind == TransactionKind.Expense)
                .SelectMany(x => x.Postings)
                .Where(x => x.Account.Type == AccountType.Expense && x.AmountMinor > 0)
                .Sum(x => (decimal)x.AmountMinor);
            result.Add(new ReportMonthRow(start, $"{year:0000}/{monthNumber:00}",
                decimal.ToInt64(monthIncome), decimal.ToInt64(monthExpense)));
            year = nextYear;
            monthNumber = nextMonth;
        }

        return result;
    }
}
