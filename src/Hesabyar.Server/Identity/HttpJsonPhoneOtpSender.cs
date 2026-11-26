using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hesabyar.Application.Identity;

namespace Hesabyar.Server.Identity;

public sealed class HttpJsonPhoneOtpSender : IPhoneOtpSender
{
    private readonly HttpClient _httpClient;
    private readonly SmsProviderOptions _options;

    public HttpJsonPhoneOtpSender(HttpClient httpClient, SmsProviderOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        if (!string.Equals(_options.Provider, "HttpJson", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HttpJson SMS provider is not selected.");

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task SendAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_options.Endpoint!, UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                to = phoneNumber,
                message = $"Your Hesabyar verification code is {code}.",
                sender = _options.SenderId
            })
        };

        var credential = string.IsNullOrWhiteSpace(_options.ApiKeyPrefix)
            ? _options.ApiKey
            : $"{_options.ApiKeyPrefix} {_options.ApiKey}";

        if (string.Equals(_options.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(credential);
        else
            request.Headers.TryAddWithoutValidation(_options.ApiKeyHeaderName, credential);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
