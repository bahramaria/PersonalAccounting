using Hesabyar.Application.Common;
using Hesabyar.Domain.ValueObjects;

namespace Hesabyar.Application.Planning;

public sealed class BudgetService(IPlanningRepository repository, IClock clock)
{
    public async Task<Result<Guid>> CreateAsync(CreateBudgetRequest request, CancellationToken cancellationToken = default)
    {
        CurrencyCode currency;
        try { currency = new CurrencyCode(request.CurrencyCode); }
        catch (ArgumentException) { return Result<Guid>.Failure("budget.currency.invalid", "Currency code is invalid."); }
        if (request.PeriodEnd < request.PeriodStart || request.LimitMinor <= 0 || request.Note?.Length > 300)
            return Result<Guid>.Failure("budget.details.invalid", "Budget details are invalid.");
        if (!await repository.IsExpenseCategoryAsync(request.CategoryAccountId, currency.Value, cancellationToken))
            return Result<Guid>.Failure("budget.category.invalid", "An active expense category in the selected currency is required.");
        var id = await repository.CreateBudgetAsync(request with { CurrencyCode = currency.Value,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() }, clock.UtcNow, cancellationToken);
        return id is Guid value ? Result<Guid>.Success(value) :
            Result<Guid>.Failure("budget.duplicate", "A budget already exists for this category and period.");
    }

    public async Task<Result<bool>> UpdateLimitAsync(Guid budgetId, long limitMinor,
        CancellationToken cancellationToken = default)
    {
        if (budgetId == Guid.Empty || limitMinor <= 0)
            return Result<bool>.Failure("budget.details.invalid", "Budget limit must be greater than zero.");
        var updated = await repository.UpdateBudgetLimitAsync(budgetId, limitMinor, clock.UtcNow, cancellationToken);
        return updated ? Result<bool>.Success(true) :
            Result<bool>.Failure("budget.not_found", "Budget was not found.");
    }

    public async Task<Result<bool>> SetActiveAsync(Guid budgetId, bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (budgetId == Guid.Empty)
            return Result<bool>.Failure("budget.not_found", "Budget was not found.");
        var updated = await repository.SetBudgetActiveAsync(budgetId, isActive, clock.UtcNow, cancellationToken);
        return updated ? Result<bool>.Success(true) :
            Result<bool>.Failure("budget.not_found", "Budget was not found.");
    }

    public Task<IReadOnlyList<BudgetSnapshot>> GetAsync(DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate) throw new ArgumentException("The start date cannot be later than the end date.");
        return repository.GetBudgetsAsync(fromDate, toDate, cancellationToken);
    }
}
