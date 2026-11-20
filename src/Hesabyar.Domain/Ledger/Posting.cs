using Hesabyar.Domain.ValueObjects;
namespace Hesabyar.Domain.Ledger;
public sealed record Posting(Guid AccountId, Money Amount, Guid? PartyId = null, string? Memo = null);