using Hesabyar.Application.Identity;

namespace Hesabyar.Server.Identity;

public static class PhoneOtpSenderRegistration
{
    public static IServiceCollection AddPhoneOtpSender(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(SmsProviderOptions.SectionName)
            .Get<SmsProviderOptions>() ?? new SmsProviderOptions();
        options.Validate();

        services.AddHttpClient("PhoneOtp", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddSingleton(options);
        services.AddSingleton<IPhoneOtpSender>(provider =>
        {
            if (string.Equals(options.Provider, "HttpJson", StringComparison.OrdinalIgnoreCase))
            {
                var client = provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("PhoneOtp");
                return new HttpJsonPhoneOtpSender(client, options);
            }

            return new UnconfiguredPhoneOtpSender();
        });

        return services;
    }
}
