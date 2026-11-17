using System.Security.Cryptography;
using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Identity;

public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAtUtc);
public sealed record RefreshTokenUser(Guid Id, string PhoneNumber);
public sealed record RefreshTokenSessionInfo(Guid DeviceId, string DeviceName, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public enum RefreshTokenStatus { Rotated, Invalid, ReuseDetected }
public sealed record RefreshTokenRotation(RefreshTokenStatus Status, Guid UserId, IssuedRefreshToken? Token);

public sealed class RefreshTokenService(IdentityDbContext database, TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<IssuedRefreshToken> CreateAsync(
        Guid userId, Guid deviceId, string? deviceName, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || deviceId == Guid.Empty)
            throw new ArgumentException("User and device identifiers are required.");

        var now = timeProvider.GetUtcNow();
        var raw = RandomNumberGenerator.GetBytes(64);
        var value = Base64UrlEncode(raw);
        var entity = new RefreshTokenSessionEntity
        {
            Id = Guid.CreateVersion7(),
            FamilyId = Guid.CreateVersion7(),
            UserId = userId,
            DeviceId = deviceId,
            DeviceName = NormalizeDeviceName(deviceName),
            TokenHash = SHA256.HashData(raw),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime)
        };
        database.RefreshTokenSessions.Add(entity);
        await database.SaveChangesAsync(cancellationToken);
        return new IssuedRefreshToken(value, entity.ExpiresAtUtc);
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        string? token, Guid deviceId, CancellationToken cancellationToken)
    {
        if (!TryHash(token, out var hash) || deviceId == Guid.Empty)
            return new RefreshTokenRotation(RefreshTokenStatus.Invalid, Guid.Empty, null);

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        var current = await database.RefreshTokenSessions.SingleOrDefaultAsync(
            x => x.TokenHash == hash, cancellationToken);
        if (current is null)
            return new RefreshTokenRotation(RefreshTokenStatus.Invalid, Guid.Empty, null);

        if (current.RevokedAtUtc is not null)
        {
            var family = await database.RefreshTokenSessions
                .Where(x => x.FamilyId == current.FamilyId && x.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            foreach (var session in family)
                session.RevokedAtUtc = now;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenRotation(RefreshTokenStatus.ReuseDetected, current.UserId, null);
        }

        var currentTime = timeProvider.GetUtcNow();
        if (current.DeviceId != deviceId || current.ExpiresAtUtc <= currentTime)
            return new RefreshTokenRotation(RefreshTokenStatus.Invalid, Guid.Empty, null);

        var accountIsActive = await database.Users.AnyAsync(
            x => x.Id == current.UserId && x.IsActive, cancellationToken);
        if (!accountIsActive)
        {
            var family = await database.RefreshTokenSessions
                .Where(x => x.FamilyId == current.FamilyId && x.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in family)
                session.RevokedAtUtc = currentTime;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenRotation(RefreshTokenStatus.Invalid, Guid.Empty, null);
        }

        var raw = RandomNumberGenerator.GetBytes(64);
        var next = new RefreshTokenSessionEntity
        {
            Id = Guid.CreateVersion7(),
            FamilyId = current.FamilyId,
            UserId = current.UserId,
            DeviceId = current.DeviceId,
            DeviceName = current.DeviceName,
            TokenHash = SHA256.HashData(raw),
            CreatedAtUtc = currentTime,
            ExpiresAtUtc = currentTime.Add(Lifetime)
        };
        current.RevokedAtUtc = currentTime;
        current.ReplacedByTokenId = next.Id;
        database.RefreshTokenSessions.Add(next);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotation(
            RefreshTokenStatus.Rotated,
            current.UserId,
            new IssuedRefreshToken(Base64UrlEncode(raw), next.ExpiresAtUtc));
    }

    public Task<RefreshTokenUser?> GetUserIdentityAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new RefreshTokenUser(x.Id, x.PhoneNumber))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshTokenSessionInfo>> GetActiveSessionsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await database.RefreshTokenSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new RefreshTokenSessionInfo(
                x.DeviceId, x.DeviceName, x.CreatedAtUtc, x.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
        return sessions
            .GroupBy(x => x.DeviceId)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task<int> RevokeDeviceAsync(
        Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var sessions = await database.RefreshTokenSessions
            .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var session in sessions)
            session.RevokedAtUtc = now;
        await database.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    public async Task<bool> RevokeAsync(
        string? token, Guid deviceId, CancellationToken cancellationToken)
    {
        if (!TryHash(token, out var hash) || deviceId == Guid.Empty)
            return false;

        var session = await database.RefreshTokenSessions.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.DeviceId == deviceId && x.RevokedAtUtc == null,
            cancellationToken);
        if (session is null)
            return false;

        session.RevokedAtUtc = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool TryHash(string? token, out byte[] hash)
    {
        hash = [];
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            return false;
        try
        {
            var normalized = token.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            var raw = Convert.FromBase64String(normalized);
            if (raw.Length != 64)
                return false;
            hash = SHA256.HashData(raw);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string NormalizeDeviceName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown device" : value.Trim()[..Math.Min(value.Trim().Length, 80)];
}
