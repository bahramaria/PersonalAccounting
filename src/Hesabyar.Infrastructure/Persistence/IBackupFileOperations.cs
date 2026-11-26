namespace Hesabyar.Infrastructure.Persistence;

public interface IBackupFileOperations
{
    void Move(string sourcePath, string destinationPath);
}

public sealed class BackupFileOperations : IBackupFileOperations
{
    public void Move(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);
}
