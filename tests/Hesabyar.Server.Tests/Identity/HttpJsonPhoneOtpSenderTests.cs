using System.Net;
using System.Text.Json;
using Hesabyar.Server.Identity;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class HttpJsonPhoneOtpSenderTests
{
    [Fact]
    public async Task SendAsync_PostsNormalizedRecipientAndMessageWithConfiguredCredential()
    {
        string? capturedBody = null;
        string? capturedApiKey = null;
        HttpMethod? capturedMethod = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            capturedMethod = request.Method;
            capturedApiKey = request.Headers.GetValues("X-Api-Key").Single();
            capturedBody = await request.Content!.ReadAsStringAsync();
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

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("test-secret", capturedApiKey);
        using var payload = JsonDocument.Parse(capturedBody!);
        Assert.Equal("+989121234567", payload.RootElement.GetProperty("to").GetString());
        Assert.Contains("123456", payload.RootElement.GetProperty("message").GetString());
        Assert.Equal("Hesabyar", payload.RootElement.GetProperty("sender").GetString());
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenProviderRejectsRequest()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
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
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request);
    }
}
