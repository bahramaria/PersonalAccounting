namespace Hesabyar.Application.Identity;

public sealed record PhoneOtpOptions
{
    public TimeSpan CodeLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ResendCooldown { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromHours(1);
    public int MaximumAttempts { get; init; } = 5;
    public int MaximumRequestsPerWindow { get; init; } = 5;
    public int CodeLength { get; init; } = 6;

    public void Validate()
    {
        if (CodeLifetime <= TimeSpan.Zero || CodeLifetime > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(CodeLifetime));
        if (ResendCooldown < TimeSpan.FromSeconds(30) || ResendCooldown > CodeLifetime)
            throw new ArgumentOutOfRangeException(nameof(ResendCooldown));
        if (RateLimitWindow < CodeLifetime)
            throw new ArgumentOutOfRangeException(nameof(RateLimitWindow));
        if (MaximumAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MaximumAttempts));
        if (MaximumRequestsPerWindow is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(MaximumRequestsPerWindow));
        if (CodeLength != 6)
            throw new ArgumentOutOfRangeException(nameof(CodeLength));
    }
}
