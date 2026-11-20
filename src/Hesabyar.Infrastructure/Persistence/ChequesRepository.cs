using Hesabyar.Application.Cheques;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class ChequesRepository(AppDbContext db) : IChequeRepository
{
    public async Task<IReadOnlyList<ChequeSnapshot>> GetAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = from cheque in db.Set<DbCheque>().AsNoTracking()
                    join party in db.Parties.AsNoTracking() on cheque.PersonId equals party.Id into parties
                    from party in parties.DefaultIfEmpty()
                    where includeArchived || !cheque.IsArchived
                    orderby cheque.DueDate, cheque.Id
                    select new ChequeSnapshot(cheque.Id, cheque.Direction, cheque.BankName, cheque.SerialNumber,
                        cheque.SayadNumber, cheque.DueDate, cheque.PersonId, party == null ? null : party.Name,
                        cheque.AmountMinor, cheque.Note, cheque.Status, cheque.IsArchived, cheque.SettlementAccountId,
                        cheque.CurrencyCode);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Guid?> CreateAsync(CreateChequeRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var duplicate = await db.Set<DbCheque>().AnyAsync(x => x.Direction == request.Direction &&
            x.BankName == request.BankName && x.SerialNumber == request.SerialNumber, cancellationToken);
        if (duplicate) return null;
        if (request.SayadNumber is not null &&
            await db.Set<DbCheque>().AnyAsync(x => x.SayadNumber == request.SayadNumber, cancellationToken)) return null;
        if (request.PersonId is Guid personId &&
            !await db.Parties.AnyAsync(x => x.Id == personId && !x.IsArchived, cancellationToken)) return null;

        var cheque = new DbCheque
        {
            Direction = request.Direction, BankName = request.BankName, SerialNumber = request.SerialNumber,
            SayadNumber = request.SayadNumber, DueDate = request.DueDate, PersonId = request.PersonId,
            AmountMinor = request.AmountMinor, CurrencyCode = request.CurrencyCode,
            Note = request.Note, Status = ChequeStatus.Pending,
            CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
        };
        db.Add(cheque);
        await db.SaveChangesAsync(cancellationToken);
        return cheque.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateChequeRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var cheque = await db.Set<DbCheque>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null) return false;
        if (cheque.Status != ChequeStatus.Pending && request.CurrencyCode != cheque.CurrencyCode) return false;
        if (await db.Set<DbCheque>().AnyAsync(x => x.Id != id && x.Direction == cheque.Direction &&
            x.BankName == request.BankName && x.SerialNumber == request.SerialNumber, cancellationToken)) return false;
        if (request.SayadNumber is not null &&
            await db.Set<DbCheque>().AnyAsync(x => x.Id != id && x.SayadNumber == request.SayadNumber, cancellationToken)) return false;
        if (request.PersonId is Guid personId &&
            !await db.Parties.AnyAsync(x => x.Id == personId && !x.IsArchived, cancellationToken)) return false;

        cheque.BankName = request.BankName; cheque.SerialNumber = request.SerialNumber;
        cheque.SayadNumber = request.SayadNumber; cheque.DueDate = request.DueDate;
        cheque.PersonId = request.PersonId; cheque.AmountMinor = request.AmountMinor;
        cheque.CurrencyCode = request.CurrencyCode;
        cheque.Note = request.Note; cheque.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var cheque = await db.Set<DbCheque>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null) return false;
        cheque.IsDeleted = true; cheque.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ChangeStatusAsync(Guid id, ChequeStatus status, Guid? settlementAccountId,
        DateOnly date, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var unit = await db.Database.BeginTransactionAsync(cancellationToken);
        var cheque = await db.Set<DbCheque>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null) return false;

        var previous = cheque.Status;
        var postings = new List<DbPosting>();
        if (previous == ChequeStatus.Cleared && status == ChequeStatus.Returned)
        {
            if (cheque.SettlementAccountId is not Guid previousAccountId) return false;
            var clearing = await GetClearingAccountAsync(cheque.Direction, cheque.CurrencyCode, nowUtc, cancellationToken);
            postings.Add(new DbPosting { AccountId = previousAccountId, AmountMinor = cheque.Direction == ChequeDirection.Received ? -cheque.AmountMinor : cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
            postings.Add(new DbPosting { AccountId = clearing.Id, AmountMinor = cheque.Direction == ChequeDirection.Received ? cheque.AmountMinor : -cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
        }
        else if (status == ChequeStatus.Cleared)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == settlementAccountId &&
                !x.IsArchived && x.Kind != AccountKind.System && x.Type == AccountType.Asset &&
                x.CurrencyCode == cheque.CurrencyCode, cancellationToken);
            if (account is null) return false;
            var clearing = await GetClearingAccountAsync(cheque.Direction, cheque.CurrencyCode, nowUtc, cancellationToken);
            postings.Add(new DbPosting { AccountId = account.Id, AmountMinor = cheque.Direction == ChequeDirection.Received ? cheque.AmountMinor : -cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
            postings.Add(new DbPosting { AccountId = clearing.Id, AmountMinor = cheque.Direction == ChequeDirection.Received ? -cheque.AmountMinor : cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
            cheque.SettlementAccountId = account.Id;
        }
        else
        {
            var state = await GetStateAccountAsync("cheque-state", "Cheque state events", cheque.CurrencyCode, nowUtc, cancellationToken);
            var offset = await GetStateAccountAsync("cheque-state-offset", "Cheque state offset", cheque.CurrencyCode, nowUtc, cancellationToken);
            postings.Add(new DbPosting { AccountId = state.Id, AmountMinor = cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
            postings.Add(new DbPosting { AccountId = offset.Id, AmountMinor = -cheque.AmountMinor, CurrencyCode = cheque.CurrencyCode });
        }

        var transaction = new DbLedgerTransaction
        {
            BusinessDate = date, Kind = TransactionKind.ChequeEvent,
            Description = $"Cheque {cheque.SerialNumber}: {previous} -> {status}",
            Source = TransactionSource.Manual, IsConfirmed = true,
            CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc, Postings = postings
        };
        db.Transactions.Add(transaction);
        cheque.Status = status;
        cheque.SettlementTransactionId = transaction.Id;
        cheque.IsArchived = status is ChequeStatus.Cleared or ChequeStatus.Spent or ChequeStatus.Voided;
        cheque.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<DbAccount> GetClearingAccountAsync(ChequeDirection direction, string currencyCode,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var key = direction == ChequeDirection.Received ? "cheque-clearing-received" : "cheque-clearing-issued";
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.SystemKey == key && x.CurrencyCode == currencyCode, cancellationToken);
        if (account is not null) return account;
        account = new DbAccount { Name = direction == ChequeDirection.Received ? "Received cheque clearing" : "Issued cheque clearing",
            Type = AccountType.Asset, Kind = AccountKind.System, CurrencyCode = currencyCode, SystemKey = key,
            IncludeInTotal = false, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    private async Task<DbAccount> GetStateAccountAsync(string key, string name, string currencyCode,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.SystemKey == key && x.CurrencyCode == currencyCode, cancellationToken);
        if (account is not null) return account;
        account = new DbAccount { Name = name, Type = AccountType.Asset, Kind = AccountKind.System,
            CurrencyCode = currencyCode, SystemKey = key, IncludeInTotal = false, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }
}
