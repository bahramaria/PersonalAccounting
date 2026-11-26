using System.Data;
using System.Security.Cryptography;
using Hesabyar.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Persistence;

public sealed class EfPhoneOtpStore(
    IdentityDbContext database,
    TimeProvider timeProvider) : IPhoneOtpStore
{
    public async Task<PhoneOtpCreateStatus> TryCreateAsync(
        PhoneOtpChallenge challenge,
        PhoneOtpOptions options,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var rateLimit = await database.PhoneOtpRateLimits
            .SingleOrDefaultAsync(x => x.PhoneNumber == challenge.PhoneNumber, cancellationToken);

        if (rateLimit is null)
        {
            rateLimit = new PhoneOtpRateLimitEntity
            {
                PhoneNumber = challenge.PhoneNumber,
                WindowStartedAtUtc = challenge.CreatedAtUtc,
                RequestCount = 0,
                LastRequestedAtUtc = DateTimeOffset.MinValue
            };
            database.PhoneOtpRateLimits.Add(rateLimit);
        }

        if (challenge.CreatedAtUtc - rateLimit.LastRequestedAtUtc < options.ResendCooldown)
            return PhoneOtpCreateStatus.Cooldown;

        if (challenge.CreatedAtUtc - rateLimit.WindowStartedAtUtc >= options.RateLimitWindow)
        {
            rateLimit.WindowStartedAtUtc = challenge.CreatedAtUtc;
            rateLimit.RequestCount = 0;
        }

        if (rateLimit.RequestCount >= options.MaximumRequestsPerWindow)
            return PhoneOtpCreateStatus.RateLimited;

        rateLimit.RequestCount++;
        rateLimit.LastRequestedAtUtc = challenge.CreatedAtUtc;
        database.PhoneOtpChallenges.Add(new PhoneOtpChallengeEntity
        {
            Id = challenge.ChallengeId,
            PhoneNumber = challenge.PhoneNumber,
            CodeHash = challenge.CodeHash,
            CreatedAtUtc = challenge.CreatedAtUtc,
            ExpiresAtUtc = challenge.ExpiresAtUtc
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PhoneOtpCreateStatus.Created;
    }

    public async Task<PhoneOtpVerificationStatus> VerifyAndConsumeAsync(
        Guid challengeId,
        string phoneNumber,
        byte[] submittedCodeHash,
        DateTimeOffset nowUtc,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var challenge = await database.PhoneOtpChallenges.SingleOrDefaultAsync(
            x => x.Id == challengeId && x.PhoneNumber == phoneNumber, cancellationToken);

        if (challenge is null || challenge.CancelledAtUtc is not null ||
            challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= nowUtc)
            return PhoneOtpVerificationStatus.InvalidOrExpired;
        if (challenge.FailedAttempts >= maximumAttempts)
            return PhoneOtpVerificationStatus.Locked;

        if (challenge.CodeHash.Length == submittedCodeHash.Length &&
            CryptographicOperations.FixedTimeEquals(challenge.CodeHash, submittedCodeHash))
        {
            challenge.ConsumedAtUtc = nowUtc;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PhoneOtpVerificationStatus.Verified;
        }

        challenge.FailedAttempts++;
        var status = challenge.FailedAttempts >= maximumAttempts
            ? PhoneOtpVerificationStatus.Locked
            : PhoneOtpVerificationStatus.InvalidOrExpired;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    public async Task CancelAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var challenge = await database.PhoneOtpChallenges
            .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);
        if (challenge is null || challenge.ConsumedAtUtc is not null)
            return;
        challenge.CancelledAtUtc = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
    }
}
