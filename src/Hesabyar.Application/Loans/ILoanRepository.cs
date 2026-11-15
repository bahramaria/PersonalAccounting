namespace Hesabyar.Application.Loans;

public interface ILoanRepository
{
    Task<Guid?> CreateAsync(CreateLoanRequest request, IReadOnlyList<ManualInstallmentRequest> schedule,
        DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<LoanSnapshot>> GetLoansAsync(CancellationToken cancellationToken);
    Task<bool> PayAsync(Guid loanId, Guid accountId, long amountMinor, DateOnly paymentDate,
        DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
