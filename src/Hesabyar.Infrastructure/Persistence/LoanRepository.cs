using Hesabyar.Application.Loans;
using Hesabyar.Domain.Accounts;
using Hesabyar.Domain.Ledger;
using Hesabyar.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class LoanRepository(AppDbContext db) : ILoanRepository
{
    public async Task<Guid?> CreateAsync(CreateLoanRequest request, IReadOnlyList<ManualInstallmentRequest> schedule,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var cashAccount = await db.Accounts.SingleOrDefaultAsync(x => x.Id == request.AccountId &&
            !x.IsArchived && x.Kind != AccountKind.System && x.Type == AccountType.Asset &&
            cancellationToken);
        if (cashAccount is null) return null;
        if (request.PersonId is Guid personId &&
            !await db.Parties.AnyAsync(x => x.Id == personId && !x.IsArchived, cancellationToken)) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loanId = Guid.CreateVersion7();
        var loanAccount = new DbAccount
        {
            Name = $"{(request.Direction == LoanDirection.Given ? "Loan receivable: " : "Loan payable: ")}{(request.Name.Length > 100 ? request.Name[..100] : request.Name)}",
            Type = request.Direction == LoanDirection.Given ? AccountType.Asset : AccountType.Liability,
            Kind = AccountKind.System, CurrencyCode = cashAccount.CurrencyCode, SystemKey = loanId.ToString("N"),
            IncludeInTotal = true, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
        };
        db.Accounts.Add(loanAccount);
        await db.SaveChangesAsync(cancellationToken);

        var loan = new DbLoan
        {
            Id = loanId, Name = request.Name, Direction = request.Direction, ScheduleType = request.ScheduleType,
            PrincipalMinor = request.PrincipalMinor, AnnualRateBasisPoints = request.AnnualRateBasisPoints,
            StartDate = request.StartDate, AccountId = cashAccount.Id, LoanAccountId = loanAccount.Id,
            PersonId = request.PersonId, Note = request.Note, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
            Installments = schedule.Select((row, index) => new DbLoanInstallment
            {
                Sequence = index + 1, DueDate = row.DueDate, PrincipalDueMinor = row.PrincipalMinor,
                InterestDueMinor = row.InterestMinor, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
            }).ToList()
        };
        db.Loans.Add(loan);

        var cashAmount = request.Direction == LoanDirection.Given ? -request.PrincipalMinor : request.PrincipalMinor;
        var loanAmount = -cashAmount;
        db.Transactions.Add(new DbLedgerTransaction
        {
            BusinessDate = request.StartDate, Kind = TransactionKind.LoanOpening, LoanId = loanId,
            PartyId = request.PersonId, Description = request.Note ?? request.Name, Source = TransactionSource.Manual,
            IsConfirmed = true, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
            Postings =
            [
                new DbPosting { AccountId = cashAccount.Id, AmountMinor = cashAmount, CurrencyCode = cashAccount.CurrencyCode },
                new DbPosting { AccountId = loanAccount.Id, AmountMinor = loanAmount, CurrencyCode = cashAccount.CurrencyCode }
            ]
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return loan.Id;
    }

    public async Task<IReadOnlyList<LoanSnapshot>> GetLoansAsync(CancellationToken cancellationToken)
    {
        var loans = await db.Loans.AsNoTracking().Include(x => x.Installments)
            .OrderBy(x => x.StartDate).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var accountIds = loans.Select(x => x.AccountId).Distinct().ToArray();
        var personIds = loans.Where(x => x.PersonId != null).Select(x => x.PersonId!.Value).Distinct().ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(x => accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.CurrencyCode }, cancellationToken);
        var people = await db.Parties.AsNoTracking().Where(x => personIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return loans.Select(loan =>
        {
            var account = accounts.GetValueOrDefault(loan.AccountId);
            return new LoanSnapshot(loan.Id, loan.Name, loan.Direction, loan.ScheduleType,
                loan.PrincipalMinor, loan.AnnualRateBasisPoints, loan.StartDate,
                account?.Name ?? string.Empty, account?.CurrencyCode ?? "IRR",
                loan.PersonId is Guid id ? people.GetValueOrDefault(id) : null, loan.Note, loan.IsClosed,
                loan.Installments.OrderBy(x => x.DueDate).ThenBy(x => x.Sequence)
                    .Select(x => new LoanInstallmentSnapshot(x.Id, x.Sequence, x.DueDate, x.PrincipalDueMinor,
                        x.InterestDueMinor, x.PaidPrincipalMinor, x.PaidInterestMinor)).ToArray());
        }).ToArray();
    }

    public async Task<bool> PayAsync(Guid loanId, Guid accountId, long amountMinor, DateOnly paymentDate,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var loan = await db.Loans.Include(x => x.Installments)
            .SingleOrDefaultAsync(x => x.Id == loanId && !x.IsClosed, cancellationToken);
        var loanCurrencyCode = loan is null
            ? null
            : await db.Accounts.Where(x => x.Id == loan.AccountId)
                .Select(x => x.CurrencyCode).SingleOrDefaultAsync(cancellationToken);
        var cashAccount = loanCurrencyCode is null
            ? null
            : await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId && !x.IsArchived &&
                x.Kind != AccountKind.System && x.Type == AccountType.Asset &&
                x.CurrencyCode == loanCurrencyCode, cancellationToken);
        if (loan is null || cashAccount is null || loanCurrencyCode is null || paymentDate < loan.StartDate) return false;

        var remainingBalance = loan.Installments.Sum(x =>
            (decimal)(x.PrincipalDueMinor - x.PaidPrincipalMinor) + x.InterestDueMinor - x.PaidInterestMinor);
        if (amountMinor > remainingBalance || amountMinor <= 0) return false;

        var amountLeft = (decimal)amountMinor;
        long principalPaid = 0, interestPaid = 0;
        foreach (var installment in loan.Installments.OrderBy(x => x.DueDate).ThenBy(x => x.Sequence))
        {
            var interestRemaining = installment.InterestDueMinor - installment.PaidInterestMinor;
            var interestPart = decimal.ToInt64(Math.Min(amountLeft, interestRemaining));
            installment.PaidInterestMinor += interestPart;
            interestPaid = checked(interestPaid + interestPart);
            amountLeft -= interestPart;

            var principalRemaining = installment.PrincipalDueMinor - installment.PaidPrincipalMinor;
            var principalPart = decimal.ToInt64(Math.Min(amountLeft, principalRemaining));
            installment.PaidPrincipalMinor += principalPart;
            principalPaid = checked(principalPaid + principalPart);
            amountLeft -= principalPart;
            if (amountLeft == 0) break;
        }
        if (amountLeft != 0) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var postings = new List<DbPosting>();
        var cashAmount = loan.Direction == LoanDirection.Given ? amountMinor : -amountMinor;
        postings.Add(new DbPosting { AccountId = cashAccount.Id, AmountMinor = cashAmount, CurrencyCode = loanCurrencyCode });
        if (principalPaid > 0)
            postings.Add(new DbPosting { AccountId = loan.LoanAccountId,
                AmountMinor = loan.Direction == LoanDirection.Given ? -principalPaid : principalPaid, CurrencyCode = loanCurrencyCode });
        if (interestPaid > 0)
        {
            var income = loan.Direction == LoanDirection.Given;
            var key = income ? "loan-interest-income" : "loan-interest-expense";
            var interestAccount = await db.Accounts.SingleOrDefaultAsync(x => x.SystemKey == key && x.CurrencyCode == loanCurrencyCode, cancellationToken);
            if (interestAccount is null)
            {
                interestAccount = new DbAccount
                {
                    Name = income ? "Loan interest income" : "Loan interest expense",
                    Type = income ? AccountType.Income : AccountType.Expense, Kind = AccountKind.System,
                    CurrencyCode = loanCurrencyCode, SystemKey = key, IncludeInTotal = true,
                    CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
                };
                db.Accounts.Add(interestAccount);
                await db.SaveChangesAsync(cancellationToken);
            }
            postings.Add(new DbPosting { AccountId = interestAccount.Id,
                AmountMinor = income ? -interestPaid : interestPaid, CurrencyCode = loanCurrencyCode });
        }

        db.Transactions.Add(new DbLedgerTransaction
        {
            BusinessDate = paymentDate, Kind = TransactionKind.LoanPayment, LoanId = loan.Id,
            PartyId = loan.PersonId, Description = $"Loan payment: {loan.Name}",
            Source = TransactionSource.Manual, IsConfirmed = true, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
            Postings = postings
        });
        var allPaid = loan.Installments.All(x =>
            x.PaidPrincipalMinor == x.PrincipalDueMinor && x.PaidInterestMinor == x.InterestDueMinor);
        loan.IsClosed = allPaid;
        loan.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
