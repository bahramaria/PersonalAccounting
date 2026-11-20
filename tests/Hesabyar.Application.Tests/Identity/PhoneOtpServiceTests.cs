using System.Security.Cryptography;
using Hesabyar.Application.Identity;
using Xunit;

namespace Hesabyar.Application.Tests.Identity;

public sealed class PhoneOtpServiceTests
{
    [Theory]
    [InlineData("+989121234567", "+989121234567")]
    [InlineData("۰۰۹۸ ۹۱۲ ۱۲۳ ۴۵۶۷", "+989121234567")]
    [InlineData("+1 (202) 555-0123", "+12025550123")]
    public void TryNormalizeE164_NormalizesSupportedInput(string input, string expected)
    {
        Assert.True(PhoneNumberNormalizer.TryNormalizeE164(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("09121234567")]
    [InlineData("+0123456789")]
    [InlineData("+123")]
    [InlineData("+1234567890123456")]
    [InlineData("not-a-phone")]
    public void TryNormalizeE164_RejectsInvalidInput(string input)
    {
        Assert.False(PhoneNumberNormalizer.TryNormalizeE164(input, out _));
    }

    [Fact]
    public async Task RequestAndVerify_SendsSixDigitCodeAndConsumesChallenge()
    {
        var store = new FakeStore();
        var sender = new FakeSender();
        var service = CreateService(store, sender);

        var request = await service.RequestAsync("۰۰۹۸ ۹۱۲ ۱۲۳ ۴۵۶۷");

        Assert.Equal(PhoneOtpRequestStatus.Sent, request.Status);
        Assert.NotNull(request.ChallengeId);
        Assert.Matches("^[0-9]{6}$", sender.Code!);
        Assert.DoesNotContain(sender.Code!, Convert.ToHexString(store.Challenge!.CodeHash));

        var verification = await service.VerifyAsync(
            request.ChallengeId!.Value,
            "+989121234567",
            sender.Code);

        Assert.Equal(PhoneOtpVerificationStatus.Verified, verification);
        Assert.Equal(1, store.VerifyCalls);
    }

    [Fact]
    public async Task Request_DoesNotSendWhenStoreAppliesCooldown()
    {
        var store = new FakeStore { CreateStatus = PhoneOtpCreateStatus.Cooldown };
        var sender = new FakeSender();
        var service = CreateService(store, sender);

        var result = await service.RequestAsync("+989121234567");

        Assert.Equal(PhoneOtpRequestStatus.Cooldown, result.Status);
        Assert.Null(sender.Code);
    }

    [Fact]
    public async Task Request_CancelsChallengeWhenDeliveryFails()
    {
        var store = new FakeStore();
        var sender = new FakeSender { ShouldFail = true };
        var service = CreateService(store, sender);

        var result = await service.RequestAsync("+989121234567");

        Assert.Equal(PhoneOtpRequestStatus.DeliveryFailed, result.Status);
        Assert.Equal(1, store.CancelCalls);
    }

    [Fact]
    public async Task Verify_RejectsMalformedCodeBeforeStoreAccess()
    {
        var store = new FakeStore();
        var service = CreateService(store, new FakeSender());

        var result = await service.VerifyAsync(Guid.NewGuid(), "+989121234567", "12A456");

        Assert.Equal(PhoneOtpVerificationStatus.InvalidOrExpired, result);
        Assert.Equal(0, store.VerifyCalls);
    }

    [Fact]
    public void Hasher_RequiresAtLeastThirtyTwoSecretBytes()
    {
        Assert.Throws<ArgumentException>(() => new HmacPhoneOtpCodeHasher(new byte[31]));
    }

    private static PhoneOtpService CreateService(FakeStore store, FakeSender sender) =>
        new(
            store,
            sender,
            new HmacPhoneOtpCodeHasher(RandomNumberGenerator.GetBytes(32)),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-11-17T12:00:00Z")));

    private sealed class FakeStore : IPhoneOtpStore
    {
        public PhoneOtpChallenge? Challenge { get; private set; }
        public PhoneOtpCreateStatus CreateStatus { get; init; } = PhoneOtpCreateStatus.Created;
        public int VerifyCalls { get; private set; }
        public int CancelCalls { get; private set; }

        public Task<PhoneOtpCreateStatus> TryCreateAsync(
            PhoneOtpChallenge challenge,
            PhoneOtpOptions options,
            CancellationToken cancellationToken)
        {
            Challenge = challenge;
            return Task.FromResult(CreateStatus);
        }

        public Task<PhoneOtpVerificationStatus> VerifyAndConsumeAsync(
            Guid challengeId,
            string phoneNumber,
            byte[] submittedCodeHash,
            DateTimeOffset nowUtc,
            int maximumAttempts,
            CancellationToken cancellationToken)
        {
            VerifyCalls++;
            if (Challenge is null || Challenge.ChallengeId != challengeId ||
                Challenge.PhoneNumber != phoneNumber || Challenge.ExpiresAtUtc <= nowUtc)
                return Task.FromResult(PhoneOtpVerificationStatus.InvalidOrExpired);

            return Task.FromResult(CryptographicOperations.FixedTimeEquals(
                Challenge.CodeHash,
                submittedCodeHash)
                ? PhoneOtpVerificationStatus.Verified
                : PhoneOtpVerificationStatus.InvalidOrExpired);
        }

        public Task CancelAsync(Guid challengeId, CancellationToken cancellationToken)
        {
            CancelCalls++;
            if (Challenge?.ChallengeId == challengeId)
                Challenge = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender : IPhoneOtpSender
    {
        public string? Code { get; private set; }
        public bool ShouldFail { get; init; }

        public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken)
        {
            if (ShouldFail)
                throw new InvalidOperationException("Delivery failed.");

            Code = code;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
