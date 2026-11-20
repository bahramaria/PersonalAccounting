using System.Data.Common;
using Hesabyar.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hesabyar.Infrastructure.Persistence;

public sealed class SqliteConnectionPragmaInterceptor(IDatabaseKeyProvider? keyProvider = null)
    : DbConnectionInterceptor
{
    private const string PragmaCommandText = """
        PRAGMA foreign_keys = ON;
        PRAGMA busy_timeout = 5000;
        PRAGMA journal_mode = WAL;
        """;

    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ConfigureEncryption(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ConfigureEncryption(connection);
        return ValueTask.FromResult(result);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection) return;
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaCommandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void ConfigureEncryption(DbConnection connection)
    {
        if (connection is not SqliteConnection || keyProvider is null) return;
        connection.ConnectionString = SqliteConnectionStringFactory.WithSqlCipherV4(
            connection.ConnectionString, keyProvider.GetDatabasePassword());
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection) return;
        using var command = connection.CreateCommand();
        command.CommandText = PragmaCommandText;
        command.ExecuteNonQuery();
    }
}
