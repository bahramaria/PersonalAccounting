using Hesabyar.Application.Common;

namespace Hesabyar.Application.People;

public sealed class PersonService(IPeopleRepository repository, IClock clock)
{
    public Task<IReadOnlyList<PersonSnapshot>> GetPeopleAsync(bool includeArchived = false, CancellationToken cancellationToken = default) =>
        repository.GetPeopleAsync(includeArchived, cancellationToken);

    public Task<IReadOnlyList<PersonEntrySnapshot>> GetEntriesAsync(Guid personId, CancellationToken cancellationToken = default,
        string currencyCode = "IRR") =>
        repository.GetEntriesAsync(personId, currencyCode, cancellationToken);

    public async Task<Result<Guid>> CreateAsync(CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<Guid>.Failure("person.name.required", "Person name is required.");
        var name = request.Name.Trim();
        if (name.Length > 120 || request.Phone?.Length > 40 || request.Note?.Length > 500)
            return Result<Guid>.Failure("person.fields.too_long", "Person details exceed the supported length.");
        var id = await repository.CreatePersonAsync(name, Normalize(request.Phone), Normalize(request.Note), clock.UtcNow, cancellationToken);
        return Result<Guid>.Success(id);
    }

    public async Task<Result<bool>> UpdateAsync(Guid id, string name, string? phone, string? note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || phone?.Length > 40 || note?.Length > 500)
            return Result<bool>.Failure("person.fields.invalid", "Person details are invalid.");
        var updated = await repository.UpdatePersonAsync(id, name.Trim(), Normalize(phone), Normalize(note), clock.UtcNow, cancellationToken);
        return updated ? Result<bool>.Success(true) : Result<bool>.Failure("person.not_found", "Person was not found.");
    }

    public async Task<Result<bool>> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var archived = await repository.ArchivePersonAsync(id, clock.UtcNow, cancellationToken);
        return archived ? Result<bool>.Success(true) : Result<bool>.Failure("person.archive.blocked", "A person with an outstanding balance cannot be archived.");
    }

    public async Task<Result<bool>> AddDebtAsync(Guid personId, Guid accountId, long amountMinor, bool theyOweMe, DateOnly date, string? note, DateOnly? dueDate = null, CancellationToken cancellationToken = default)
    {
        if (amountMinor <= 0)
            return Result<bool>.Failure("person.amount.invalid", "Amount must be greater than zero.");
        if (note?.Length > 500)
            return Result<bool>.Failure("person.note.too_long", "Note cannot exceed 500 characters.");
        if (dueDate is not null && dueDate < date)
            return Result<bool>.Failure("person.due_date.invalid", "Due date cannot be earlier than the debt date.");
        var saved = await repository.AddDebtAsync(personId, accountId, amountMinor, theyOweMe, date, dueDate, Normalize(note), clock.UtcNow, cancellationToken);
        return saved ? Result<bool>.Success(true) : Result<bool>.Failure("person.entry.invalid", "Person or account was not found, or the account cannot be used.");
    }

    public async Task<Result<bool>> RepayAsync(Guid personId, Guid accountId, long amountMinor, DateOnly date, string? note, bool allowOverpayment = false, CancellationToken cancellationToken = default)
    {
        if (amountMinor <= 0)
            return Result<bool>.Failure("person.amount.invalid", "Amount must be greater than zero.");
        if (note?.Length > 500)
            return Result<bool>.Failure("person.note.too_long", "Note cannot exceed 500 characters.");
        var saved = await repository.RepayAsync(personId, accountId, amountMinor, date, Normalize(note), allowOverpayment, clock.UtcNow, cancellationToken);
        return saved ? Result<bool>.Success(true) : Result<bool>.Failure("person.repayment.invalid", "Repayment exceeds the outstanding balance, or person/account was not found.");
    }

    public async Task<IReadOnlyList<PersonDueSnapshot>> GetOutstandingDuesAsync(Guid personId,
        CancellationToken cancellationToken = default, string currencyCode = "IRR")
    {
        var entries = (await repository.GetEntriesAsync(personId, currencyCode, cancellationToken))
            .OrderBy(x => x.Date).ThenBy(x => x.CreatedAtUtc).ToArray();
        var obligations = new List<DueObligation>();

        foreach (var entry in entries)
        {
            if (entry.Kind == "Debt")
            {
                obligations.Add(new DueObligation(entry.DueDate, entry.AmountMinor, entry.BalanceEffectMinor > 0));
                continue;
            }

            var repaymentReducesTheirDebt = entry.BalanceEffectMinor < 0;
            var remaining = (decimal)entry.AmountMinor;
            foreach (var obligation in obligations.Where(x => x.TheyOweMe == repaymentReducesTheirDebt && x.Remaining > 0))
            {
                var applied = Math.Min(obligation.Remaining, remaining);
                obligation.Remaining -= applied;
                remaining -= applied;
                if (remaining == 0) break;
            }
        }

        return obligations.Where(x => x.DueDate is not null && x.Remaining > 0)
            .OrderBy(x => x.DueDate)
            .Select(x => new PersonDueSnapshot(x.DueDate!.Value, decimal.ToInt64(x.Remaining), x.TheyOweMe))
            .ToArray();
    }

    private sealed class DueObligation(DateOnly? dueDate, long amountMinor, bool theyOweMe)
    {
        public DateOnly? DueDate { get; } = dueDate;
        public decimal Remaining { get; set; } = amountMinor;
        public bool TheyOweMe { get; } = theyOweMe;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
