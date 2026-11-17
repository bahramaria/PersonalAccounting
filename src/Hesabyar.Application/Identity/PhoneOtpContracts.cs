namespace Hesabyar.Application.Identity;

public sealed record PhoneOtpChallenge(
    Guid ChallengeId,
    string PhoneNumber,
    byte[] CodeHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public enum PhoneOtpCreateStatus
{
    Created,
    Cooldown,
    RateLimited
}

public enum PhoneOtpVerificationStatus
{
    Verified,
    InvalidOrExpired,
    Locked
}

public enum PhoneOtpRequestStatus
{
    Sent,
    InvalidPhoneNumber,
    Cooldown,
    RateLimited,
    DeliveryFailed
}

public sealed record PhoneOtpRequestResult(
    PhoneOtpRequestStatus Status,
    Guid? ChallengeId = null);

public interface IPhoneOtpStore
{
    Task<PhoneOtpCreateStatus> TryCreateAsync(
        PhoneOtpChallenge challenge,
        PhoneOtpOptions options,
        CancellationToken cancellationToken);

    Task<PhoneOtpVerificationStatus> VerifyAndConsumeAsync(
        Guid challengeId,
        string phoneNumber,
        byte[] submittedCodeHash,
        DateTimeOffset nowUtc,
        int maximumAttempts,
        CancellationToken cancellationToken);

    Task CancelAsync(Guid challengeId, CancellationToken cancellationToken);
}

public interface IPhoneOtpSender
{
    Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken);
}

public interface IPhoneOtpCodeHasher
{
    byte[] ComputeHash(Guid challengeId, string phoneNumber, string code);
}
