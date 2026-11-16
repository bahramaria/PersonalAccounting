namespace Hesabyar.Application.Planning;

public sealed record CreateBudgetRequest(Guid CategoryAccountId, DateOnly PeriodStart, DateOnly PeriodEnd,
    long LimitMinor, string CurrencyCode, string? Note = null);
public sealed record BudgetSnapshot(Guid Id, Guid CategoryAccountId, string CategoryName,
    DateOnly PeriodStart, DateOnly PeriodEnd, long LimitMinor, long SpentMinor,
    string CurrencyCode, bool IsActive, string? Note)
{
    public long RemainingMinor => checked(LimitMinor - SpentMinor);
    public decimal UsageRatio => LimitMinor <= 0 ? 0 : (decimal)SpentMinor / LimitMinor;
}
public sealed record TagSnapshot(Guid Id, string Name, string? Color);
public sealed record FiscalPeriodSnapshot(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsClosed);
public sealed record CreateFiscalPeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate);
