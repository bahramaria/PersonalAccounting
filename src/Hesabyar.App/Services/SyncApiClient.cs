using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Hesabyar.Sync.Contracts;

namespace Hesabyar.App.Services;

public sealed class SyncApiClient(HttpClient httpClient, CloudSessionStore sessionStore)
{
    public async Task<SyncPushResponse> PushAsync(
        IReadOnlyList<SyncChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var session = await GetValidSessionAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Post, "/api/v1/sync/push", session);
        request.Content = JsonContent.Create(new SyncPushRequest(session.DeviceId, changes));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncPushResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The sync push response was empty.");
    }

    public async Task<SyncPullResponse> PullAsync(
        string? cursor,
        int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var session = await GetValidSessionAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Post, "/api/v1/sync/pull", session);
        request.Content = JsonContent.Create(new SyncPullRequest(cursor, pageSize, session.DeviceId));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncPullResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The sync pull response was empty.");
    }

    private async Task<CloudSession> GetValidSessionAsync(CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null ||
            httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Configure an HTTPS cloud API base address before synchronizing.");

        var session = await sessionStore.GetAsync();
        if (session is null)
            throw new InvalidOperationException("Sign in to the cloud account before synchronizing.");

        if (session.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            if (session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The cloud session has expired. Sign in again.");

            session = await RefreshSessionAsync(session, cancellationToken);
        }

        return session;
    }

    private async Task<CloudSession> RefreshSessionAsync(
        CloudSession session,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest(session.RefreshToken, session.DeviceId),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                "The cloud session could not be refreshed. Sign in again.",
                null,
                response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<RefreshResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The cloud refresh response was empty.");

        if (string.IsNullOrWhiteSpace(tokens.AccessToken) ||
            string.IsNullOrWhiteSpace(tokens.RefreshToken) ||
            tokens.AccessTokenExpiresAtUtc == default ||
            tokens.RefreshTokenExpiresAtUtc == default)
            throw new InvalidOperationException("The cloud refresh response is incomplete.");

        await sessionStore.UpdateTokensAsync(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc);

        return session with
        {
            AccessToken = tokens.AccessToken,
            AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
            RefreshToken = tokens.RefreshToken,
            RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc
        };
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, CloudSession session)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return request;
    }

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("deviceId")] Guid DeviceId);

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_at_utc")] DateTimeOffset AccessTokenExpiresAtUtc,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("refresh_expires_at_utc")] DateTimeOffset RefreshTokenExpiresAtUtc);
}
