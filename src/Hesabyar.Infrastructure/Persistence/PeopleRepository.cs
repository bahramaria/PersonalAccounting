using Hesabyar.Application.People;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class PeopleRepository(AppDbContext db) : IPeopleRepository
{
    private const string ClearingAccountKey = "people-clearing";

    public async Task<IReadOnlyList<PersonSnapshot>> GetPeopleAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var parties = await db.Parties.AsNoTracking().Where(x => includeArchived || !x.IsArchived)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var ids = parties.Select(x => x.Id).ToArray();
        var balances = await db.Postings.Where(x => x.PartyId != null && ids.Contains(x.PartyId.Value) &&
                x.Account.SystemKey == ClearingAccountKey && !x.Transaction.IsDeleted)
            .GroupBy(x => new { Id = x.PartyId!.Value, x.CurrencyCode })
            .Select(g => new { g.Key.Id, g.Key.CurrencyCode, Balance = g.Sum(x => x.AmountMinor) })
            .ToListAsync(cancellationToken);
        return parties.SelectMany(party =>
        {
            var personBalances = balances.Where(x => x.Id == party.Id).ToArray();
            return personBalances.Length == 0
                ? new[] { new PersonSnapshot(party.Id, party.Name, party.Phone, party.Note, 0, party.IsArchived) }
                : personBalances.Select(x => new PersonSnapshot(party.Id, party.Name, party.Phone, party.Note,
                    x.Balance, party.IsArchived, x.CurrencyCode));
        }).ToArray();
    }

    public async Task<IReadOnlyList<PersonEntrySnapshot>> GetEntriesAsync(Guid personId, string currencyCode, CancellationToken cancellationToken)
    {
        var entries = await db.Transactions.AsNoTracking().Include(x => x.Postings).ThenInclude(x => x.Account)
            .Where(x => x.PartyId == personId &&
                x.Postings.Any(p => p.PartyId == personId && p.CurrencyCode == currencyCode) &&
                (x.Kind == TransactionKind.PersonDebt || x.Kind == TransactionKind.PersonRepay))
            .OrderByDescending(x => x.BusinessDate)
            .ToListAsync(cancellationToken);
        entries = entries.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.CreatedAtUtc).ToList();
        return entries.Select(x =>
        {
            var partyPosting = x.Postings.FirstOrDefault(p => p.PartyId == personId && p.CurrencyCode == currencyCode);
            var cashPosting = x.Postings.FirstOrDefault(p => p.PartyId == null &&
                p.Account.SystemKey == null && p.CurrencyCode == currencyCode);
            return new PersonEntrySnapshot(x.Id, x.BusinessDate, x.Kind == TransactionKind.PersonDebt ? "Debt" : "Repayment",
                decimal.ToInt64(Math.Abs((decimal)(partyPosting?.AmountMinor ?? 0))), cashPosting?.Account.Name ?? string.Empty,
                x.Description, x.DueDate, partyPosting?.AmountMinor ?? 0, x.CreatedAtUtc, currencyCode);
        }).ToArray();
    }

    public async Task<Guid> CreatePersonAsync(string name, string? phone, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var party = new DbParty { Name = name, Phone = phone, Note = note, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        db.Parties.Add(party);
        await db.SaveChangesAsync(cancellationToken);
        return party.Id;
    }

    public async Task<bool> UpdatePersonAsync(Guid personId, string name, string? phone, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var party = await db.Parties.SingleOrDefaultAsync(x => x.Id == personId && !x.IsArchived, cancellationToken);
        if (party is null) return false;
        party.Name = name; party.Phone = phone; party.Note = note; party.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ArchivePersonAsync(Guid personId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var party = await db.Parties.SingleOrDefaultAsync(x => x.Id == personId && !x.IsArchived, cancellationToken);
        if (party is null) return false;
        var balances = await db.Postings
            .Where(x => x.PartyId == personId && x.Account.SystemKey == ClearingAccountKey && !x.Transaction.IsDeleted)
            .GroupBy(x => x.CurrencyCode)
            .Select(group => group.Sum(x => x.AmountMinor))
            .ToListAsync(cancellationToken);
        if (balances.Any(x => x != 0)) return false;
        party.IsArchived = true; party.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> AddDebtAsync(Guid personId, Guid accountId, long amountMinor, bool theyOweMe, DateOnly date, DateOnly? dueDate, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        SaveEntryAsync(personId, accountId, amountMinor, theyOweMe ? amountMinor : -amountMinor, TransactionKind.PersonDebt, date, dueDate, note, nowUtc, cancellationToken);

    public async Task<bool> RepayAsync(Guid personId, Guid accountId, long amountMinor, DateOnly date, string? note, bool allowOverpayment, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && !x.IsArchived &&
            x.Kind != AccountKind.System && x.Type == AccountType.Asset, cancellationToken);
        if (account is null) return false;
        var balance = await GetBalanceAsync(personId, account.CurrencyCode, cancellationToken);
        if (balance == 0) return false;
        var outstanding = Math.Abs((decimal)balance);
        if (amountMinor > outstanding && !allowOverpayment) return false;

        var excessAmount = amountMinor > outstanding ? decimal.ToInt64(amountMinor - outstanding) : 0L;
        var repaymentAmount = amountMinor - excessAmount;
        var clearingAmount = balance > 0 ? -repaymentAmount : repaymentAmount;
        var excessClearingAmount = balance > 0 ? -excessAmount : excessAmount;
        return await SaveEntryAsync(personId, accountId, repaymentAmount, clearingAmount, TransactionKind.PersonRepay,
            date, null, note, nowUtc, cancellationToken, excessAmount, excessClearingAmount);
    }

    private async Task<long> GetBalanceAsync(Guid personId, string currencyCode, CancellationToken cancellationToken) =>
        await db.Postings.Where(x => x.PartyId == personId && x.CurrencyCode == currencyCode &&
                x.Account.SystemKey == ClearingAccountKey && !x.Transaction.IsDeleted)
            .Select(x => (long?)x.AmountMinor).SumAsync(cancellationToken) ?? 0L;

    private async Task<bool> SaveEntryAsync(Guid personId, Guid accountId, long amountMinor, long clearingAmount,
        TransactionKind kind, DateOnly date, DateOnly? dueDate, string? note, DateTimeOffset nowUtc, CancellationToken cancellationToken,
        long excessAmountMinor = 0, long excessClearingAmount = 0)
    {
        var party = await db.Parties.SingleOrDefaultAsync(x => x.Id == personId && !x.IsArchived, cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && !x.IsArchived &&
            !x.IsDeleted && x.Kind != AccountKind.System, cancellationToken);
        if (party is null || account is null || account.Type != AccountType.Asset) return false;
        var currencyCode = account.CurrencyCode;

        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var clearing = await db.Accounts.SingleOrDefaultAsync(x => x.SystemKey == ClearingAccountKey &&
            x.CurrencyCode == currencyCode, cancellationToken);
        if (clearing is null)
        {
            clearing = new DbAccount { Name = "People clearing", Type = AccountType.Asset, Kind = AccountKind.System,
                CurrencyCode = currencyCode, SystemKey = ClearingAccountKey, IncludeInTotal = false,
                CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
            db.Accounts.Add(clearing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var transaction = new DbLedgerTransaction
        {
            BusinessDate = date, DueDate = dueDate, Kind = kind, Description = note, PartyId = personId,
            Source = TransactionSource.Manual, IsConfirmed = true, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
            Postings =
            [
                new DbPosting { AccountId = clearing.Id, AmountMinor = clearingAmount, CurrencyCode = currencyCode, PartyId = personId },
                new DbPosting { AccountId = accountId, AmountMinor = -clearingAmount, CurrencyCode = currencyCode }
            ]
        };
        db.Transactions.Add(transaction);
        if (excessAmountMinor > 0)
        {
            db.Transactions.Add(new DbLedgerTransaction
            {
                BusinessDate = date,
                Kind = TransactionKind.PersonDebt,
                Description = note,
                PartyId = personId,
                Source = TransactionSource.Manual,
                IsConfirmed = true,
                CreatedAtUtc = nowUtc.AddTicks(1),
                UpdatedAtUtc = nowUtc.AddTicks(1),
                Postings =
                [
                    new DbPosting { AccountId = clearing.Id, AmountMinor = excessClearingAmount, CurrencyCode = currencyCode, PartyId = personId },
                    new DbPosting { AccountId = accountId, AmountMinor = -excessClearingAmount, CurrencyCode = currencyCode }
                ]
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return true;
    }
}
