namespace Hesabyar.Application.Security;

public interface IKeyProvider
{
    ValueTask<byte[]> GetDatabaseKeyAsync(CancellationToken cancellationToken);
}
