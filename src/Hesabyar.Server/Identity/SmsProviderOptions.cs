namespace Hesabyar.Server.Identity;

public sealed class SmsProviderOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; set; } = "Disabled";
    public string? Endpoint { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "Authorization";
    public string ApiKeyPrefix { get; set; } = "Bearer";
    public string? SenderId { get; set; }
    public int TimeoutSeconds { get; set; } = 15;

    public void Validate()
    {
        if (string.Equals(Provider, "Disabled", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(Provider, "HttpJson", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sms:Provider must be Disabled or HttpJson.");

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Sms:Endpoint must be an absolute HTTPS URL.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Sms:ApiKey must be configured.");

        if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
            throw new InvalidOperationException("Sms:ApiKeyHeaderName must be configured.");

        if (TimeoutSeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds));
    }
}
