namespace Hesabyar.Infrastructure.Security;

public interface IDatabaseKeyInitializer
{
    Task InitializeAsync(string migrationBackupDirectory, CancellationToken cancellationToken = default);
}
