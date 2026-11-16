using Hesabyar.Application.Planning;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class PlanningRepository(AppDbContext db) : IPlanningRepository
{
    public Task<bool> IsExpenseCategoryAsync(Guid categoryId, string currencyCode, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(x => x.Id == categoryId && !x.IsArchived &&
            x.Kind == AccountKind.Category && x.Type == AccountType.Expense &&
            x.CurrencyCode == currencyCode, cancellationToken);

    public async Task<Guid?> CreateBudgetAsync(CreateBudgetRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (await db.Budgets.AnyAsync(x => x.CategoryAccountId == request.CategoryAccountId &&
            x.PeriodStart == request.PeriodStart && x.PeriodEnd == request.PeriodEnd, cancellationToken))
            return null;
        var entity = new DbBudget
        {
            Id = Guid.CreateVersion7(), CategoryAccountId = request.CategoryAccountId,
            PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd,
            LimitMinor = request.LimitMinor, CurrencyCode = request.CurrencyCode,
            IsActive = true, Note = request.Note, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
        };
        db.Budgets.Add(entity);
        try { await db.SaveChangesAsync(cancellationToken); return entity.Id; }
        catch (DbUpdateException) { db.Entry(entity).State = EntityState.Detached; return null; }
    }

    public async Task<IReadOnlyList<BudgetSnapshot>> GetBudgetsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var budgets = await db.Budgets.AsNoTracking().Include(x => x.CategoryAccount)
            .Where(x => x.IsActive && x.PeriodStart <= toDate && x.PeriodEnd >= fromDate)
            .OrderBy(x => x.PeriodStart).ThenBy(x => x.CategoryAccount.Name)
            .ToListAsync(cancellationToken);
        var result = new List<BudgetSnapshot>(budgets.Count);
        foreach (var budget in budgets)
        {
            var spent = await db.Postings.AsNoTracking()
                .Where(x => x.AccountId == budget.CategoryAccountId && x.CurrencyCode == budget.CurrencyCode &&
                    x.AmountMinor > 0 && x.Transaction.Kind == TransactionKind.Expense &&
                    x.Transaction.IsConfirmed && !x.Transaction.IsDeleted &&
                    x.Transaction.BusinessDate >= budget.PeriodStart && x.Transaction.BusinessDate <= budget.PeriodEnd)
                .Select(x => (long?)x.AmountMinor).SumAsync(cancellationToken) ?? 0L;
            result.Add(new BudgetSnapshot(budget.Id, budget.CategoryAccountId, budget.CategoryAccount.Name,
                budget.PeriodStart, budget.PeriodEnd, budget.LimitMinor, spent, budget.CurrencyCode,
                budget.IsActive, budget.Note));
        }
        return result;
    }

    public async Task<IReadOnlyList<TagSnapshot>> GetTagsAsync(CancellationToken cancellationToken) =>
        await db.Tags.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new TagSnapshot(x.Id, x.Name, x.Color)).ToArrayAsync(cancellationToken);

    public async Task<Guid?> SaveTagAsync(Guid? tagId, string name, string? color, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var duplicate = await db.Tags.AnyAsync(x => x.Name.ToLower() == name.ToLower() &&
            (tagId == null || x.Id != tagId.Value), cancellationToken);
        if (duplicate) return null;
        if (tagId is Guid id)
        {
            var existing = await db.Tags.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (existing is null) return null;
            existing.Name = name; existing.Color = color; existing.UpdatedAtUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
            return id;
        }
        var entity = new DbTag { Id = Guid.CreateVersion7(), Name = name, Color = color,
            CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        db.Tags.Add(entity);
        try { await db.SaveChangesAsync(cancellationToken); return entity.Id; }
        catch (DbUpdateException) { db.Entry(entity).State = EntityState.Detached; return null; }
    }

    public async Task<bool> SetTransactionTagsAsync(Guid transactionId, IReadOnlyCollection<Guid> tagIds,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (!await db.Transactions.AnyAsync(x => x.Id == transactionId, cancellationToken)) return false;
        if (tagIds.Count > 0 && await db.Tags.CountAsync(x => tagIds.Contains(x.Id), cancellationToken) != tagIds.Count)
            return false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.TransactionTags.Where(x => x.TransactionId == transactionId).ToListAsync(cancellationToken);
        db.TransactionTags.RemoveRange(existing);
        foreach (var tagId in tagIds.Distinct())
            db.TransactionTags.Add(new DbTransactionTag { Id = Guid.CreateVersion7(), TransactionId = transactionId,
                TagId = tagId, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<FiscalPeriodSnapshot>> GetFiscalPeriodsAsync(CancellationToken cancellationToken) =>
        await db.FiscalPeriods.AsNoTracking().OrderBy(x => x.StartDate)
            .Select(x => new FiscalPeriodSnapshot(x.Id, x.Name, x.StartDate, x.EndDate, x.IsClosed))
            .ToArrayAsync(cancellationToken);

    public Task<bool> HasOverlappingFiscalPeriodAsync(DateOnly startDate, DateOnly endDate, Guid? exceptId,
        CancellationToken cancellationToken) =>
        db.FiscalPeriods.AnyAsync(x => (!exceptId.HasValue || x.Id != exceptId.Value) &&
            x.StartDate <= endDate && x.EndDate >= startDate, cancellationToken);

    public async Task<Guid?> CreateFiscalPeriodAsync(CreateFiscalPeriodRequest request, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await HasOverlappingFiscalPeriodAsync(request.StartDate, request.EndDate, null, cancellationToken))
            return null;
        var period = new DbFiscalPeriod { Id = Guid.CreateVersion7(), Name = request.Name,
            StartDate = request.StartDate, EndDate = request.EndDate, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        db.FiscalPeriods.Add(period);
        db.DocumentSequences.Add(new DbDocumentSequence { Id = Guid.CreateVersion7(), FiscalPeriodId = period.Id,
            LastNumber = 0, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return period.Id;
    }

    public async Task<bool> CloseFiscalPeriodAsync(Guid periodId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var period = await db.FiscalPeriods.SingleOrDefaultAsync(x => x.Id == periodId && !x.IsClosed, cancellationToken);
        if (period is null) return false;
        period.IsClosed = true; period.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
