namespace Hesabyar.Application.People;

public interface IPeopleRepository
{
    Task<IReadOnlyList<PersonSnapshot>> GetPeopleAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersonEntrySnapshot>> GetEntriesAsync(Guid personId, CancellationToken cancellationToken);
    Task<Guid> CreatePersonAsync(string name, string? phone, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> UpdatePersonAsync(Guid personId, string name, string? phone, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> ArchivePersonAsync(Guid personId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> AddDebtAsync(Guid personId, Guid accountId, long amountMinor, bool theyOweMe, DateOnly date, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> RepayAsync(Guid personId, Guid accountId, long amountMinor, DateOnly date, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
