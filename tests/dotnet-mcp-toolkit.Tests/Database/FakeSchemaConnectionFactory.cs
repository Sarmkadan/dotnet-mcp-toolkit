using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

/// <summary>
/// Serves metadata collections from memory, standing in for a non-SQLite provider's <c>GetSchema</c> output.
/// Only <see cref="DbConnection.GetSchema(string)"/> is implemented; every other member throws.
/// </summary>
internal sealed class FakeSchemaConnectionFactory(Func<string, DataTable> collections) : IDbConnectionFactory
{
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    public ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<DbConnection>(new FakeSchemaConnection(collections));

    private sealed class FakeSchemaConnection(Func<string, DataTable> collections) : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string ServerVersion => "fake";

        public override string DataSource => "fake";

        public override string Database => "fake";

        public override ConnectionState State => ConnectionState.Open;

        public override DataTable GetSchema(string collectionName) => collections(collectionName);

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
