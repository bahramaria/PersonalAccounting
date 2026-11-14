using Microsoft.Data.Sqlite;

namespace Hesabyar.Infrastructure.Persistence;

public static class SqliteConnectionStringFactory
{
    public static string CreateSqlCipherV4Uri(string databasePath, string? password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var uri = new Uri(Path.GetFullPath(databasePath)).AbsoluteUri;
        var key = password is null ? string.Empty : Uri.EscapeDataString(password);
        return uri + "?cipher=sqlcipher&legacy=4&key=" + key;
    }

    public static string CreatePlainSqliteUri(string databasePath) =>
        new Uri(Path.GetFullPath(databasePath)).AbsoluteUri;

    public static string WithSqlCipherV4(string connectionString, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (!dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            builder.DataSource = new Uri(Path.GetFullPath(dataSource)).AbsoluteUri + "?cipher=sqlcipher&legacy=4";
        else if (!dataSource.Contains("cipher=sqlcipher", StringComparison.OrdinalIgnoreCase))
            builder.DataSource = dataSource + (dataSource.Contains('?') ? "&" : "?") + "cipher=sqlcipher&legacy=4";
        builder.Password = password;
        return builder.ToString();
    }
}
