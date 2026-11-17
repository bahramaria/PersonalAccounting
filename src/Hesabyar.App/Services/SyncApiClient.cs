using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        cancellationToken.ThrowIfCancellationRequested();
        if (httpClient.BaseAddress is null ||
            httpClient.BaseAddress.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Configure an HTTPS cloud API base address before synchronizing.");

        var session = await sessionStore.GetAsync();
        if (session is null)
            throw new InvalidOperationException("Sign in to the cloud account before synchronizing.");

        if (session.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The cloud access token has expired. Refresh the session before synchronizing.");

        return session;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, CloudSession session)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return request;
    }
}
