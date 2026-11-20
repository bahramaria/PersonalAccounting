using Hesabyar.Server.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hesabyar.Server.Identity;

public sealed class PhoneOtpAccountService(IdentityDbContext database)
{
    public async Task<Guid> GetOrCreateAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var existing = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PhoneNumber == phoneNumber, cancellationToken);
        if (existing is not null)
            return existing.Id;

        var user = new IdentityUserEntity
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = phoneNumber,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true
        };
        database.Users.Add(user);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException)
        {
            database.Entry(user).State = EntityState.Detached;
            var concurrentUser = await database.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PhoneNumber == phoneNumber, cancellationToken);
            if (concurrentUser is not null)
                return concurrentUser.Id;
            throw;
        }
    }
}
