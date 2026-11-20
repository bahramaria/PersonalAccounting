using Hesabyar.Server.Persistence;
using Hesabyar.Server.Sync;
using Hesabyar.Sync.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hesabyar.Server.Tests.Sync;

public sealed class SyncServiceIntegrationTests
{
    [Fact]
    public async Task PushAsync_IsIdempotentAndRejectsChangedPayloadForSameChangeId()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var userId = await database.AddUserAndDeviceAsync();
            var deviceId = database.DeviceId;
            var service = database.CreateService();
            var change = CreateChange(deviceId, 1);

            var first = await service.PushAsync(userId, new SyncPushRequest(deviceId, [change]), CancellationToken.None);
            Assert.Equal(new[] { change.ChangeId }, first.AcceptedChangeIds);
            Assert.Empty(first.DuplicateChangeIds);

            var replay = await service.PushAsync(userId, new SyncPushRequest(deviceId, [change]), CancellationToken.None);
            Assert.Empty(replay.AcceptedChangeIds);
            Assert.Equal(new[] { change.ChangeId }, replay.DuplicateChangeIds);

            var altered = change with { Fields = [SyncFieldValue.From("Name", "Altered")] };
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.PushAsync(userId, new SyncPushRequest(deviceId, [altered]), CancellationToken.None));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task PushAsync_RejectsSequenceRegressionAndDoesNotPersistPartialBatch()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var userId = await database.AddUserAndDeviceAsync();
            var deviceId = database.DeviceId;
            var service = database.CreateService();
            var first = CreateChange(deviceId, 4);
            await service.PushAsync(userId, new SyncPushRequest(deviceId, [first]), CancellationToken.None);

            var invalidBatch = new SyncPushRequest(deviceId,
                [CreateChange(deviceId, 5), CreateChange(deviceId, 4)]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.PushAsync(userId, invalidBatch, CancellationToken.None));

            await using var verification = database.CreateContext();
            Assert.Equal(1, await verification.SyncChangeLogs.CountAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task PushAsync_RejectsDeviceNotBoundToAuthenticatedUser()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var userId = await database.AddUserAndDeviceAsync();
            var unregisteredDevice = Guid.NewGuid();
            var service = database.CreateService();

            await Assert.ThrowsAsync<SyncDeviceNotRegisteredException>(() =>
                service.PushAsync(userId,
                    new SyncPushRequest(unregisteredDevice, [CreateChange(unregisteredDevice, 1)]),
                    CancellationToken.None));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task PullAsync_ReturnsOrderedPagesAndRejectsCursorFromAnotherUser()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var userId = await database.AddUserAndDeviceAsync();
            var deviceId = database.DeviceId;
            var service = database.CreateService();
            var changes = new[] { CreateChange(deviceId, 1), CreateChange(deviceId, 2), CreateChange(deviceId, 3) };
            await service.PushAsync(userId, new SyncPushRequest(deviceId, changes), CancellationToken.None);

            var first = await service.PullAsync(userId, new SyncPullRequest(null, 2, deviceId), CancellationToken.None);
            Assert.True(first.HasMore);
            Assert.Equal(2, first.Changes.Count);
            Assert.NotNull(first.NextCursor);

            var second = await service.PullAsync(userId,
                new SyncPullRequest(first.NextCursor, 2, deviceId), CancellationToken.None);
            Assert.False(second.HasMore);
            Assert.Single(second.Changes);
            Assert.Equal(changes[2].ChangeId, second.Changes[0].ChangeId);

            var cursorParts = first.NextCursor!.Split('.');
            Assert.Equal(2, cursorParts.Length);
            var alteredSignature = (cursorParts[1][0] == 'A' ? "B" : "A") + cursorParts[1][1..];
            var tamperedCursor = $"{cursorParts[0]}.{alteredSignature}";
            await Assert.ThrowsAsync<SyncCursorException>(() =>
                service.PullAsync(userId,
                    new SyncPullRequest(tamperedCursor, 2, deviceId), CancellationToken.None));

            var (anotherUser, anotherDevice) = await database.AddAnotherUserAndDeviceAsync();
            await Assert.ThrowsAsync<SyncCursorException>(() =>
                service.PullAsync(anotherUser,
                    new SyncPullRequest(first.NextCursor, 2, anotherDevice), CancellationToken.None));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task PushAndPull_RejectRevokedDeviceSession()
    {
        var database = await TestDatabase.CreateAsync();
        try
        {
            var userId = await database.AddUserAndDeviceAsync();
            var deviceId = database.DeviceId;
            var service = database.CreateService();
            await service.PushAsync(
                userId,
                new SyncPushRequest(deviceId, [CreateChange(deviceId, 1)]),
                CancellationToken.None);

            await using (var context = database.CreateContext())
            {
                var session = await context.RefreshTokenSessions
                    .SingleAsync(x => x.UserId == userId && x.DeviceId == deviceId);
                session.RevokedAtUtc = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<SyncDeviceNotRegisteredException>(() =>
                service.PushAsync(
                    userId,
                    new SyncPushRequest(deviceId, [CreateChange(deviceId, 2)]),
                    CancellationToken.None));

            await Assert.ThrowsAsync<SyncDeviceNotRegisteredException>(() =>
                service.PullAsync(
                    userId,
                    new SyncPullRequest(null, 100, deviceId),
                    CancellationToken.None));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static SyncChange CreateChange(Guid deviceId, long sequence) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Account", SyncOperation.Upsert, 1,
            new SyncVersionStamp(DateTimeOffset.Parse("2026-11-17T10:00:00Z").AddSeconds(sequence), deviceId, sequence),
            [SyncFieldValue.From("Name", $"Account {sequence}"), SyncFieldValue.From("BalanceMinorUnits", sequence * 100)]);

    private sealed class TestDatabase(string databaseName) : IAsyncDisposable
    {
        private const string ConnectionPrefix =
            "Server=localhost,1433;User Id=sa;Password=Your_password123!;Encrypt=False;TrustServerCertificate=True;";
        private readonly List<IdentityDbContext> _serviceContexts = [];
        private readonly DbContextOptions<IdentityDbContext> _options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer($"{ConnectionPrefix}Database={databaseName};")
                .Options;

        public Guid DeviceId { get; } = Guid.NewGuid();

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase($"HesabyarSyncTests_{Guid.NewGuid():N}");
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public IdentityDbContext CreateContext() => new(_options);

        public SyncService CreateService()
        {
            var context = CreateContext();
            _serviceContexts.Add(context);
            return new SyncService(
                context,
                TimeProvider.System,
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("Hesabyar.Sync.IntegrationTests.Secret.v1")));
        }

        public async Task<(Guid UserId, Guid DeviceId)> AddAnotherUserAndDeviceAsync()
        {
            var user = new IdentityUserEntity
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+989{Random.Shared.Next(100000000, 999999999)}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsActive = true
            };
            var deviceId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            context.Users.Add(user);
            context.RefreshTokenSessions.Add(new RefreshTokenSessionEntity
            {
                Id = Guid.NewGuid(),
                FamilyId = Guid.NewGuid(),
                UserId = user.Id,
                DeviceId = deviceId,
                DeviceName = "Second sync integration test",
                TokenHash = System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30)
            });
            await context.SaveChangesAsync();
            return (user.Id, deviceId);
        }

        public async Task<Guid> AddUserAndDeviceAsync()
        {
            var user = new IdentityUserEntity
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+989{Random.Shared.Next(100000000, 999999999)}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsActive = true
            };
            var now = DateTimeOffset.UtcNow;
            await using var context = CreateContext();
            context.Users.Add(user);
            context.RefreshTokenSessions.Add(new RefreshTokenSessionEntity
            {
                Id = Guid.NewGuid(),
                FamilyId = Guid.NewGuid(),
                UserId = user.Id,
                DeviceId = DeviceId,
                DeviceName = "Sync integration test",
                TokenHash = System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30)
            });
            await context.SaveChangesAsync();
            return user.Id;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var serviceContext in _serviceContexts)
                await serviceContext.DisposeAsync();
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }
}
