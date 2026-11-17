namespace Hesabyar.Application.Planning;

public interface IPlanningRepository
{
    Task<bool> IsExpenseCategoryAsync(Guid categoryId, string currencyCode, CancellationToken cancellationToken);
    Task<Guid?> CreateBudgetAsync(CreateBudgetRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> UpdateBudgetLimitAsync(Guid budgetId, long limitMinor, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> SetBudgetActiveAsync(Guid budgetId, bool isActive, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<BudgetSnapshot>> GetBudgetsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<TagSnapshot>> GetTagsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetTransactionTagIdsAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<Guid?> SaveTagAsync(Guid? tagId, string name, string? color, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> SetTransactionTagsAsync(Guid transactionId, IReadOnlyCollection<Guid> tagIds, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<FiscalPeriodSnapshot>> GetFiscalPeriodsAsync(CancellationToken cancellationToken);
    Task<bool> HasOverlappingFiscalPeriodAsync(DateOnly startDate, DateOnly endDate, Guid? exceptId, CancellationToken cancellationToken);
    Task<Guid?> CreateFiscalPeriodAsync(CreateFiscalPeriodRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> CloseFiscalPeriodAsync(Guid periodId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
