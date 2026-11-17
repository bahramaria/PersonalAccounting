using Hesabyar.Application.Identity;
using Hesabyar.Server.Identity;
using Hesabyar.Server.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

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
builder.Services.AddSingleton(new PhoneOtpTokenIssuer(tokenSecret, TimeSpan.FromMinutes(15)));

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
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseRateLimiter();

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
    PhoneOtpTokenIssuer tokens,
    CancellationToken cancellationToken) =>
{
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
    return Results.Ok(new
    {
        token_type = "Bearer",
        access_token = token.Value,
        expires_at_utc = token.ExpiresAtUtc,
        user_id = userId,
        phone_number = phone
    });
}).RequireRateLimiting("otp-ip");

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.Run();

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
public sealed record PhoneOtpVerifyRequest(Guid ChallengeId, string? PhoneNumber, string? Code);
