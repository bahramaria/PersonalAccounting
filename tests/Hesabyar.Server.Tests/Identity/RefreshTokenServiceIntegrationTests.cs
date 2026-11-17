using Hesabyar.Server.Identity;
using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class RefreshTokenServiceIntegrationTests
{
    [Fact]
    public async Task RotateAsync_RotatesTokenAndRejectsOldToken()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-11-17T12:00:00Z"));
            var userId = await database.AddActiveUserAsync();
            var deviceId = Guid.NewGuid();
            var initial = await database.CreateService(clock).CreateAsync(
                userId, deviceId, "Integration test device", CancellationToken.None);

            var rotated = await database.CreateService(clock).RotateAsync(
                initial.Value, deviceId, CancellationToken.None);

            Assert.Equal(RefreshTokenStatus.Rotated, rotated.Status);
            Assert.NotNull(rotated.Token);
            Assert.NotEqual(initial.Value, rotated.Token!.Value);

            var replay = await database.CreateService(clock).RotateAsync(
                initial.Value, deviceId, CancellationToken.None);

            Assert.Equal(RefreshTokenStatus.ReuseDetected, replay.Status);

            var revokedReplacement = await database.CreateService(clock).RotateAsync(
                rotated.Token.Value, deviceId, CancellationToken.None);
            Assert.Equal(RefreshTokenStatus.ReuseDetected, revokedReplacement.Status);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task RotateAsync_ConcurrentUseOfSameTokenRevokesTheTokenFamily()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-11-17T12:00:00Z"));
            var userId = await database.AddActiveUserAsync();
            var deviceId = Guid.NewGuid();
            var initial = await database.CreateService(clock).CreateAsync(
                userId, deviceId, "Concurrent test device", CancellationToken.None);

            var firstRequest = database.CreateService(clock).RotateAsync(
                initial.Value, deviceId, CancellationToken.None);
            var secondRequest = database.CreateService(clock).RotateAsync(
                initial.Value, deviceId, CancellationToken.None);
            var results = await Task.WhenAll(firstRequest, secondRequest);

            Assert.Single(results, result => result.Status == RefreshTokenStatus.Rotated);
            Assert.Single(results, result => result.Status == RefreshTokenStatus.ReuseDetected);

            await using var verification = database.CreateContext();
            var family = await verification.RefreshTokenSessions
                .Where(x => x.UserId == userId)
                .ToListAsync();
            Assert.Equal(2, family.Count);
            Assert.All(family, session => Assert.NotNull(session.RevokedAtUtc));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task RotateAsync_RejectsTokenFromAnotherDeviceWithoutConsumingIt()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-11-17T12:00:00Z"));
            var userId = await database.AddActiveUserAsync();
            var deviceId = Guid.NewGuid();
            var initial = await database.CreateService(clock).CreateAsync(
                userId, deviceId, "Device binding test", CancellationToken.None);

            var rejected = await database.CreateService(clock).RotateAsync(
                initial.Value, Guid.NewGuid(), CancellationToken.None);
            Assert.Equal(RefreshTokenStatus.Invalid, rejected.Status);

            var valid = await database.CreateService(clock).RotateAsync(
                initial.Value, deviceId, CancellationToken.None);
            Assert.Equal(RefreshTokenStatus.Rotated, valid.Status);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private sealed class TestDatabase(string databaseName) : IAsyncDisposable
    {
        private const string ConnectionPrefix =
            "Server=localhost,1433;User Id=sa;Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;";

        private readonly DbContextOptions<IdentityDbContext> _options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer($"{ConnectionPrefix}Database={databaseName};")
                .Options;

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase($"HesabyarRefreshTests_{Guid.NewGuid():N}");
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public IdentityDbContext CreateContext() => new(_options);

        public RefreshTokenService CreateService(TimeProvider clock) =>
            new(CreateContext(), clock);

        public async Task<Guid> AddActiveUserAsync()
        {
            var user = new IdentityUserEntity
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+989{Random.Shared.Next(100000000, 999999999)}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsActive = true
            };
            await using var context = CreateContext();
            context.Users.Add(user);
            await context.SaveChangesAsync();
            return user.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = CreateContext();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
