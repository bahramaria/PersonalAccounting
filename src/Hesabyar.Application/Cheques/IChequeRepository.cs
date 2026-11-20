namespace Hesabyar.Application.Cheques;

public interface IChequeRepository
{
    Task<IReadOnlyList<ChequeSnapshot>> GetAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<Guid?> CreateAsync(CreateChequeRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(Guid id, UpdateChequeRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<bool> ChangeStatusAsync(Guid id, ChequeStatus status, Guid? settlementAccountId, DateOnly date, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
