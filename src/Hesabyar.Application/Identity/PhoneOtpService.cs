using System.Globalization;
using System.Security.Cryptography;

namespace Hesabyar.Application.Identity;

public sealed class PhoneOtpService
{
    private readonly IPhoneOtpStore _store;
    private readonly IPhoneOtpSender _sender;
    private readonly IPhoneOtpCodeHasher _hasher;
    private readonly TimeProvider _timeProvider;
    private readonly PhoneOtpOptions _options;

    public PhoneOtpService(
        IPhoneOtpStore store,
        IPhoneOtpSender sender,
        IPhoneOtpCodeHasher hasher,
        TimeProvider timeProvider,
        PhoneOtpOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? new PhoneOtpOptions();
        _options.Validate();
    }

    public async Task<PhoneOtpRequestResult> RequestAsync(
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (!PhoneNumberNormalizer.TryNormalizeE164(phoneNumber, out var normalizedPhone))
            return new PhoneOtpRequestResult(PhoneOtpRequestStatus.InvalidPhoneNumber);

        var now = _timeProvider.GetUtcNow();
        var challengeId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(CultureInfo.InvariantCulture);
        var challenge = new PhoneOtpChallenge(
            challengeId,
            normalizedPhone,
            _hasher.ComputeHash(challengeId, normalizedPhone, code),
            now,
            now.Add(_options.CodeLifetime));

        var createStatus = await _store.TryCreateAsync(challenge, _options, cancellationToken);
        if (createStatus != PhoneOtpCreateStatus.Created)
        {
            var status = createStatus switch
            {
                PhoneOtpCreateStatus.Cooldown => PhoneOtpRequestStatus.Cooldown,
                PhoneOtpCreateStatus.RateLimited => PhoneOtpRequestStatus.RateLimited,
                _ => throw new ArgumentOutOfRangeException(nameof(createStatus))
            };
            return new PhoneOtpRequestResult(status);
        }

        try
        {
            await _sender.SendAsync(normalizedPhone, code, cancellationToken);
            return new PhoneOtpRequestResult(PhoneOtpRequestStatus.Sent, challengeId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _store.CancelAsync(challengeId, CancellationToken.None);
            throw;
        }
        catch
        {
            await _store.CancelAsync(challengeId, CancellationToken.None);
            return new PhoneOtpRequestResult(PhoneOtpRequestStatus.DeliveryFailed);
        }
    }

    public async Task<PhoneOtpVerificationStatus> VerifyAsync(
        Guid challengeId,
        string? phoneNumber,
        string? code,
        CancellationToken cancellationToken = default)
    {
        if (challengeId == Guid.Empty ||
            !PhoneNumberNormalizer.TryNormalizeE164(phoneNumber, out var normalizedPhone) ||
            !TryNormalizeCode(code, out var normalizedCode))
            return PhoneOtpVerificationStatus.InvalidOrExpired;

        var codeHash = _hasher.ComputeHash(challengeId, normalizedPhone, normalizedCode);
        return await _store.VerifyAndConsumeAsync(
            challengeId,
            normalizedPhone,
            codeHash,
            _timeProvider.GetUtcNow(),
            _options.MaximumAttempts,
            cancellationToken);
    }

    private static bool TryNormalizeCode(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        Span<char> buffer = stackalloc char[6];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '-' or ' ')
                continue;

            var digit = character switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                _ => character
            };

            if (digit is < '0' or > '9' || count == buffer.Length)
                return false;

            buffer[count++] = digit;
        }

        if (count != buffer.Length)
            return false;

        normalized = new string(buffer);
        return true;
    }
}
