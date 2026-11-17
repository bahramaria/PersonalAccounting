using Hesabyar.Application.Common;

namespace Hesabyar.Application.Cheques;

public sealed class ChequeService(IChequeRepository repository, IClock clock)
{
    public async Task<Result<IReadOnlyList<ChequeSnapshot>>> GetAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
        => Result<IReadOnlyList<ChequeSnapshot>>.Success(await repository.GetAsync(includeArchived, cancellationToken));

    public async Task<Result<Guid>> CreateAsync(CreateChequeRequest request, CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.Direction, request.BankName, request.SerialNumber, request.SayadNumber,
            request.DueDate, request.AmountMinor, request.Note, request.CurrencyCode);
        if (validation is not null)
            return Result<Guid>.Failure("cheque.invalid", validation);
        var id = await repository.CreateAsync(request with
        {
            BankName = request.BankName.Trim(),
            SerialNumber = request.SerialNumber.Trim(),
            SayadNumber = Normalize(request.SayadNumber),
            Note = Normalize(request.Note),
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant()
        }, clock.UtcNow, cancellationToken);
        return id is Guid value
            ? Result<Guid>.Success(value)
            : Result<Guid>.Failure("cheque.duplicate", "A cheque with this bank and serial number already exists.");
    }

    public async Task<Result<bool>> UpdateAsync(Guid id, UpdateChequeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BankName) || request.BankName.Trim().Length > 80 ||
            string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Trim().Length > 40 ||
            request.SayadNumber?.Trim().Length > 24 || request.AmountMinor <= 0 ||
            request.Note?.Length > 500 || !IsValidCurrencyCode(request.CurrencyCode))
            return Result<bool>.Failure("cheque.invalid", "Cheque details are invalid.");
        var updated = await repository.UpdateAsync(id, request with
        {
            BankName = request.BankName.Trim(), SerialNumber = request.SerialNumber.Trim(),
            SayadNumber = Normalize(request.SayadNumber), Note = Normalize(request.Note),
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant()
        }, clock.UtcNow, cancellationToken);
        return updated ? Result<bool>.Success(true) :
            Result<bool>.Failure("cheque.update.failed", "Cheque was not found or its serial number is duplicated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await repository.DeleteAsync(id, clock.UtcNow, cancellationToken);
        return deleted ? Result<bool>.Success(true) : Result<bool>.Failure("cheque.not_found", "Cheque was not found.");
    }

    public async Task<Result<bool>> ChangeStatusAsync(Guid id, ChequeStatus status, Guid? settlementAccountId = null, CancellationToken cancellationToken = default)
    {
        var items = await repository.GetAsync(true, cancellationToken);
        var cheque = items.SingleOrDefault(x => x.Id == id);
        if (cheque is null)
            return Result<bool>.Failure("cheque.not_found", "Cheque was not found.");

        var allowed = cheque.Status switch
        {
            ChequeStatus.Pending => status is ChequeStatus.Cleared or ChequeStatus.Returned or ChequeStatus.Spent or ChequeStatus.Voided,
            ChequeStatus.Returned => status is ChequeStatus.Cleared or ChequeStatus.Voided,
            ChequeStatus.Cleared => status == ChequeStatus.Returned,
            _ => false
        };
        if (!allowed)
            return Result<bool>.Failure("cheque.transition.invalid", "This cheque status transition is not allowed.");

        if (status == ChequeStatus.Cleared && settlementAccountId is null)
            return Result<bool>.Failure("cheque.account.required", "Select the bank account used to clear this cheque.");

        var changed = await repository.ChangeStatusAsync(id, status, settlementAccountId,
            DateOnly.FromDateTime(DateTime.Today), clock.UtcNow, cancellationToken);
        return changed ? Result<bool>.Success(true) :
            Result<bool>.Failure("cheque.status.failed", "The cheque status could not be changed.");
    }

    private static string? Validate(ChequeDirection direction, string bank, string serial, string? sayad,
        DateOnly dueDate, long amount, string? note, string currencyCode)
    {
        if (!Enum.IsDefined(direction) || string.IsNullOrWhiteSpace(bank) || bank.Trim().Length > 80 ||
            string.IsNullOrWhiteSpace(serial) || serial.Trim().Length > 40 ||
            sayad?.Trim().Length > 24 || amount <= 0 || note?.Length > 500 ||
            dueDate < new DateOnly(1900, 1, 1) || dueDate > new DateOnly(2200, 12, 31) ||
            !IsValidCurrencyCode(currencyCode))
            return "Cheque details are invalid.";
        return null;
    }

    private static bool IsValidCurrencyCode(string? value) =>
        value is { Length: 3 } && value.All(char.IsAsciiLetter);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
