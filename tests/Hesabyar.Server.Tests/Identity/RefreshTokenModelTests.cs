using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hesabyar.Server.Tests.Identity;

public sealed class RefreshTokenModelTests
{
    [Fact]
    public void RefreshTokenSession_UsesHashedUniqueTokenAndUserCascade()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=(local);Database=HesabyarIdentityTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var database = new IdentityDbContext(options);
        var entity = database.Model.FindEntityType(typeof(RefreshTokenSessionEntity));

        Assert.NotNull(entity);
        Assert.Equal("RefreshTokenSessions", entity!.GetTableName());

        var tokenHash = entity.FindProperty(nameof(RefreshTokenSessionEntity.TokenHash));
        Assert.NotNull(tokenHash);
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(RefreshTokenSessionEntity.TokenHash));

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(IdentityUserEntity), foreignKey.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void RefreshTokenSession_HasIndexesForDeviceAndFamilyQueries()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=(local);Database=HesabyarIdentityTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var database = new IdentityDbContext(options);
        var entity = database.Model.FindEntityType(typeof(RefreshTokenSessionEntity));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(RefreshTokenSessionEntity.UserId), nameof(RefreshTokenSessionEntity.DeviceId)]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Single().Name == nameof(RefreshTokenSessionEntity.FamilyId));
    }
}
