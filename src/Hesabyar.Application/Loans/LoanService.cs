using Hesabyar.Application.Common;

namespace Hesabyar.Application.Loans;

public sealed class LoanService(ILoanRepository repository, IClock clock)
{
    public async Task<Result<Guid>> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Direction) || !Enum.IsDefined(request.ScheduleType) ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120 ||
            request.Note?.Length > 500 || request.PrincipalMinor <= 0)
            return Result<Guid>.Failure("loan.details.invalid", "Loan details are invalid.");

        if (request.InstallmentCount is < 1 or > 600 || request.AnnualRateBasisPoints is < 0 or > 100_000)
            return Result<Guid>.Failure("loan.schedule.invalid", "Loan schedule settings are invalid.");

        var schedule = request.ScheduleType switch
        {
            LoanScheduleType.Annuity when request.ManualSchedule is null =>
                BuildAnnuitySchedule(request),
            LoanScheduleType.Manual when request.ManualSchedule is not null =>
                ValidateManualSchedule(request),
            _ => null
        };
        if (schedule is null)
            return Result<Guid>.Failure("loan.schedule.invalid", "The installment schedule is invalid.");

        var id = await repository.CreateAsync(request with { Name = request.Name.Trim(),
            Note = Normalize(request.Note) }, schedule, clock.UtcNow, cancellationToken);
        return id is Guid value
            ? Result<Guid>.Success(value)
            : Result<Guid>.Failure("loan.create.invalid", "The selected account or person is unavailable.");
    }

    public Task<IReadOnlyList<LoanSnapshot>> GetLoansAsync(CancellationToken cancellationToken = default) =>
        repository.GetLoansAsync(cancellationToken);

    public async Task<Result<bool>> PayAsync(Guid loanId, Guid accountId, long amountMinor,
        DateOnly paymentDate, CancellationToken cancellationToken = default)
    {
        if (amountMinor <= 0)
            return Result<bool>.Failure("loan.payment.invalid", "Payment amount must be greater than zero.");
        var saved = await repository.PayAsync(loanId, accountId, amountMinor, paymentDate, clock.UtcNow, cancellationToken);
        return saved ? Result<bool>.Success(true) :
            Result<bool>.Failure("loan.payment.invalid", "Payment exceeds the remaining balance or the loan/account is unavailable.");
    }

    private static IReadOnlyList<ManualInstallmentRequest>? ValidateManualSchedule(CreateLoanRequest request)
    {
        var rows = request.ManualSchedule;
        if (rows is null || rows.Count != request.InstallmentCount ||
            rows.Any(x => x.DueDate <= request.StartDate || x.PrincipalMinor <= 0 || x.InterestMinor < 0))
            return null;
        try
        {
            return rows.Sum(x => (decimal)x.PrincipalMinor) == request.PrincipalMinor
                ? rows.OrderBy(x => x.DueDate).ToArray()
                : null;
        }
        catch (OverflowException) { return null; }
    }

    private static IReadOnlyList<ManualInstallmentRequest>? BuildAnnuitySchedule(CreateLoanRequest request)
    {
        try
        {
            var monthlyRate = request.AnnualRateBasisPoints / 10_000m / 12m;
            var payment = request.PrincipalMinor / (decimal)request.InstallmentCount;
            if (monthlyRate > 0)
            {
                var factor = 1m;
                for (var i = 0; i < request.InstallmentCount; i++) factor *= 1m + monthlyRate;
                payment = decimal.Round(request.PrincipalMinor * monthlyRate * factor / (factor - 1m),
                    0, MidpointRounding.AwayFromZero);
            }

            var remaining = (decimal)request.PrincipalMinor;
            var result = new List<ManualInstallmentRequest>(request.InstallmentCount);
            for (var i = 1; i <= request.InstallmentCount; i++)
            {
                var interest = decimal.ToInt64(decimal.Round(remaining * monthlyRate, 0, MidpointRounding.AwayFromZero));
                var principal = i == request.InstallmentCount
                    ? decimal.ToInt64(remaining)
                    : decimal.ToInt64(Math.Min(remaining, Math.Max(1m, payment - interest)));
                remaining -= principal;
                if (remaining < 0) return null;
                result.Add(new ManualInstallmentRequest(request.StartDate.AddMonths(i), principal, interest));
            }
            return remaining == 0 ? result : null;
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
