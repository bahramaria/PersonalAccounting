using Hesabyar.Application.Identity;

namespace Hesabyar.Server.Identity;

public sealed class UnconfiguredPhoneOtpSender : IPhoneOtpSender
{
    public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "No SMS provider is configured. Configure an IPhoneOtpSender implementation before enabling OTP delivery.");
}
