using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hesabyar.Server.Identity;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class HttpJsonPhoneOtpSenderTests
{
    [Fact]
    public async Task SendAsync_PostsNormalizedRecipientAndMessageWithConfiguredCredential()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var client = new HttpClient(handler);
        var options = new SmsProviderOptions
        {
            Provider = "HttpJson",
            Endpoint = "https://sms.example.test/api/messages",
            ApiKey = "test-secret",
            ApiKeyHeaderName = "X-Api-Key",
            ApiKeyPrefix = string.Empty,
            SenderId = "Hesabyar"
        };
        var sender = new HttpJsonPhoneOtpSender(client, options);

        await sender.SendAsync("+989121234567", "123456", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("test-secret", captured.Headers.GetValues("X-Api-Key").Single());
        using var payload = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync());
        Assert.Equal("+989121234567", payload.RootElement.GetProperty("to").GetString());
        Assert.Contains("123456", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal("Hesabyar", payload.RootElement.GetProperty("sender").GetString());
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenProviderRejectsRequest()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var sender = new HttpJsonPhoneOtpSender(client, new SmsProviderOptions
        {
            Provider = "HttpJson",
            Endpoint = "https://sms.example.test/api/messages",
            ApiKey = "test-secret"
        });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("+989121234567", "123456", CancellationToken.None));
    }

    [Fact]
    public void Validate_RejectsNonHttpsProviderEndpoint()
    {
        var options = new SmsProviderOptions
        {
            Provider = "HttpJson",
            Endpoint = "http://sms.example.test/api/messages",
            ApiKey = "test-secret"
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
