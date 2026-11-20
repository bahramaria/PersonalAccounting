using Hesabyar.Application.Common;

namespace Hesabyar.Application.Planning;

public sealed class FiscalPeriodService(IPlanningRepository repository, IClock clock)
{
    public Task<IReadOnlyList<FiscalPeriodSnapshot>> GetAsync(CancellationToken cancellationToken = default) =>
        repository.GetFiscalPeriodsAsync(cancellationToken);

    public async Task<Result<Guid>> CreateAsync(CreateFiscalPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80 ||
            request.EndDate < request.StartDate)
            return Result<Guid>.Failure("fiscal_period.details.invalid", "Fiscal period details are invalid.");
        if (await repository.HasOverlappingFiscalPeriodAsync(request.StartDate, request.EndDate, null, cancellationToken))
            return Result<Guid>.Failure("fiscal_period.overlap", "Fiscal periods cannot overlap.");
        var id = await repository.CreateFiscalPeriodAsync(request with { Name = request.Name.Trim() },
            clock.UtcNow, cancellationToken);
        return id is Guid value ? Result<Guid>.Success(value) :
            Result<Guid>.Failure("fiscal_period.create.failed", "The fiscal period could not be created.");
    }

    public async Task<Result<bool>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var saved = await repository.CloseFiscalPeriodAsync(id, clock.UtcNow, cancellationToken);
        return saved ? Result<bool>.Success(true) :
            Result<bool>.Failure("fiscal_period.close.failed", "The fiscal period was not found or is already closed.");
    }
}
