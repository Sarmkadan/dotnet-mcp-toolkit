using System.Data.Common;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// Opens database connections for the database tool pack. Implement it to plug in a custom provider.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Gets the database engine the connections point to.
    /// </summary>
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Creates and opens a new connection. The caller owns the returned connection and disposes it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An open connection.</returns>
    ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken);
}
