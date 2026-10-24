using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// Opens SQLite connections through Microsoft.Data.Sqlite.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates a factory for the given SQLite connection string, e.g. <c>Data Source=app.db;Mode=ReadOnly</c>.
    /// </summary>
    /// <param name="connectionString">ADO.NET connection string.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null, empty or whitespace.</exception>
    public SqliteConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
