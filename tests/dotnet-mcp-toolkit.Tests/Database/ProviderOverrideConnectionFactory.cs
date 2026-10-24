using System.Data.Common;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

/// <summary>
/// Wraps a SQLite connection factory but reports another provider, to exercise provider-specific code paths
/// without a second database engine.
/// </summary>
internal sealed class ProviderOverrideConnectionFactory(DatabaseProvider provider, string connectionString) : IDbConnectionFactory
{
    private readonly SqliteConnectionFactory _inner = new(connectionString);

    public DatabaseProvider Provider { get; } = provider;

    public ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
}
