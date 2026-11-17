using Hesabyar.Application.Identity;
using Hesabyar.Server.Identity;
using Hesabyar.Server.Persistence;
using Hesabyar.Server.Sync;
using Hesabyar.Sync.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Identity");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Identity must be configured.");

var otpSecret = ReadSecret(builder.Configuration["Identity:OtpHmacSecret"], "Identity:OtpHmacSecret");
var tokenSecret = ReadSecret(builder.Configuration["Identity:AccessTokenSecret"], "Identity:AccessTokenSecret");
var otpOptions = builder.Configuration.GetSection("Identity:Otp").Get<PhoneOtpOptions>() ?? new PhoneOtpOptions();
otpOptions.Validate();

builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<PhoneOtpOptions>(otpOptions);
builder.Services.AddSingleton<IPhoneOtpCodeHasher>(_ => new HmacPhoneOtpCodeHasher(otpSecret));
builder.Services.AddSingleton<IPhoneOtpSender, UnconfiguredPhoneOtpSender>();
builder.Services.AddScoped<IPhoneOtpStore, EfPhoneOtpStore>();
builder.Services.AddScoped<PhoneOtpService>();
builder.Services.AddScoped<PhoneOtpAccountService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddSingleton(tokenSecret);
builder.Services.AddSingleton(new PhoneOtpTokenIssuer(tokenSecret, TimeSpan.FromMinutes(15)));
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, AccessTokenAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("otp-ip", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("sync-ip", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await database.Database.MigrateAsync();
}

app.MapPost("/api/v1/auth/otp/request", async (
    PhoneOtpRequest request,
    PhoneOtpService otpService,
    CancellationToken cancellationToken) =>
{
    var result = await otpService.RequestAsync(request.PhoneNumber, cancellationToken);
    return result.Status switch
    {
        PhoneOtpRequestStatus.Sent => Results.Accepted(
            $"/api/v1/auth/otp/challenges/{result.ChallengeId}",
            new { challenge_id = result.ChallengeId, expires_in_seconds = (int)otpOptions.CodeLifetime.TotalSeconds }),
        PhoneOtpRequestStatus.InvalidPhoneNumber => Results.BadRequest(new { error = "invalid_phone_number" }),
        PhoneOtpRequestStatus.Cooldown => Results.Problem(statusCode: 429, title: "otp_cooldown"),
        PhoneOtpRequestStatus.RateLimited => Results.Problem(statusCode: 429, title: "otp_rate_limited"),
        PhoneOtpRequestStatus.DeliveryFailed => Results.Problem(statusCode: 503, title: "otp_delivery_unavailable"),
        _ => Results.Problem(statusCode: 500)
    };
}).RequireRateLimiting("otp-ip");

app.MapPost("/api/v1/auth/otp/verify", async (
    PhoneOtpVerifyRequest request,
    PhoneOtpService otpService,
    PhoneOtpAccountService accounts,
    RefreshTokenService refreshTokens,
    PhoneOtpTokenIssuer tokens,
    CancellationToken cancellationToken) =>
{
    if (request.DeviceId == Guid.Empty)
        return Results.BadRequest(new { error = "invalid_device_id" });

    var result = await otpService.VerifyAsync(
        request.ChallengeId, request.PhoneNumber, request.Code, cancellationToken);
    if (result != PhoneOtpVerificationStatus.Verified)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "otp_invalid_or_expired");
    }

    var phone = PhoneNumberNormalizer.TryNormalizeE164(request.PhoneNumber, out var normalized)
        ? normalized
        : throw new InvalidOperationException("The verified phone number could not be normalized.");
    var userId = await accounts.GetOrCreateAsync(phone, cancellationToken);
    var token = tokens.Issue(userId, phone);
    var refreshToken = await refreshTokens.CreateAsync(
        userId, request.DeviceId, request.DeviceName, cancellationToken);
    return Results.Ok(new
    {
        token_type = "Bearer",
        access_token = token.Value,
        expires_at_utc = token.ExpiresAtUtc,
        refresh_token = refreshToken.Value,
        refresh_expires_at_utc = refreshToken.ExpiresAtUtc,
        user_id = userId,
        phone_number = phone
    });
}).RequireRateLimiting("otp-ip");

app.MapPost("/api/v1/auth/refresh", async (
    RefreshTokenRequest request,
    RefreshTokenService refreshTokens,
    PhoneOtpTokenIssuer tokens,
    CancellationToken cancellationToken) =>
{
    var rotation = await refreshTokens.RotateAsync(
        request.RefreshToken, request.DeviceId, cancellationToken);
    if (rotation.Status != RefreshTokenStatus.Rotated || rotation.Token is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: rotation.Status == RefreshTokenStatus.ReuseDetected
                ? "refresh_token_reuse_detected"
                : "refresh_token_invalid");
    }

    var user = await refreshTokens.GetUserIdentityAsync(rotation.UserId, cancellationToken);
    if (user is null)
    {
        await refreshTokens.RevokeAsync(rotation.Token.Value, request.DeviceId, cancellationToken);
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "account_unavailable");
    }

    var accessToken = tokens.Issue(user.Id, user.PhoneNumber);
    return Results.Ok(new
    {
        token_type = "Bearer",
        access_token = accessToken.Value,
        expires_at_utc = accessToken.ExpiresAtUtc,
        refresh_token = rotation.Token.Value,
        refresh_expires_at_utc = rotation.Token.ExpiresAtUtc
    });
}).RequireRateLimiting("otp-ip");

app.MapPost("/api/v1/auth/logout", async (
    RefreshTokenRequest request,
    RefreshTokenService refreshTokens,
    CancellationToken cancellationToken) =>
{
    var revoked = await refreshTokens.RevokeAsync(
        request.RefreshToken, request.DeviceId, cancellationToken);
    return revoked ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/v1/auth/sessions", async (
    ClaimsPrincipal principal,
    RefreshTokenService refreshTokens,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();
    var sessions = await refreshTokens.GetActiveSessionsAsync(userId, cancellationToken);
    return Results.Ok(sessions);
}).RequireAuthorization();

app.MapDelete("/api/v1/auth/sessions/{deviceId:guid}", async (
    Guid deviceId,
    ClaimsPrincipal principal,
    RefreshTokenService refreshTokens,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();
    await refreshTokens.RevokeDeviceAsync(userId, deviceId, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/v1/sync/push", async (
    SyncPushRequest request,
    ClaimsPrincipal principal,
    SyncService sync,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();
    try
    {
        return Results.Ok(await sync.PushAsync(userId, request, cancellationToken));
    }
    catch (SyncDeviceNotRegisteredException)
    {
        return Results.Unauthorized();
    }
    catch (SyncSequenceConflictException exception)
    {
        return Results.Conflict(new { error = "sync_sequence_conflict", detail = exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = "invalid_sync_request", detail = exception.Message });
    }
}).RequireAuthorization().RequireRateLimiting("sync-ip");

app.MapPost("/api/v1/sync/pull", async (
    SyncPullRequest request,
    ClaimsPrincipal principal,
    SyncService sync,
    CancellationToken cancellationToken) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();
    try
    {
        return Results.Ok(await sync.PullAsync(userId, request, cancellationToken));
    }
    catch (SyncDeviceNotRegisteredException)
    {
        return Results.Unauthorized();
    }
    catch (SyncCursorException exception)
    {
        return Results.BadRequest(new { error = "invalid_sync_cursor", detail = exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = "invalid_sync_request", detail = exception.Message });
    }
}).RequireAuthorization().RequireRateLimiting("sync-ip");

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.Run();

static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
    Guid.TryParseExact(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, "N", out userId);

static byte[] ReadSecret(string? value, string key)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"{key} must be configured as a Base64-encoded secret.");
    byte[] secret;
    try
    {
        secret = Convert.FromBase64String(value);
    }
    catch (FormatException exception)
    {
        throw new InvalidOperationException($"{key} must be valid Base64.", exception);
    }
    if (secret.Length < 32)
        throw new InvalidOperationException($"{key} must decode to at least 32 bytes.");
    return secret;
}

public sealed record PhoneOtpRequest(string? PhoneNumber);
public sealed record PhoneOtpVerifyRequest(
    Guid ChallengeId, string? PhoneNumber, string? Code, Guid DeviceId, string? DeviceName);
public sealed record RefreshTokenRequest(string? RefreshToken, Guid DeviceId);
