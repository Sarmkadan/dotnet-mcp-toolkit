using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderDatabaseTests
{
    [Fact]
    public void AddDatabase_Sqlite_RegistersSqlQueryTool()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDatabase("Data Source=:memory:", DatabaseProvider.Sqlite);

        Assert.Equal(["sql_query"], builder.Tools.Select(t => t.Name));
    }

    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.MySql)]
    public void AddDatabase_ProviderNotInCore_ThrowsNotSupported(DatabaseProvider provider)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        var ex = Assert.Throws<NotSupportedException>(() => builder.AddDatabase("Host=localhost", provider));

        Assert.Equal($"Provider {provider} requires the DotnetMcpToolkit.Database.{provider} package", ex.Message);
    }

    [Fact]
    public void AddDatabase_CustomFactory_RegistersSqlQueryTool()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDatabase(new SqliteConnectionFactory("Data Source=:memory:"), allowWrites: true);

        Assert.Equal(["sql_query"], builder.Tools.Select(t => t.Name));
    }

    [Fact]
    public void AddDatabase_ReturnsSameBuilder()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Same(builder, builder.AddDatabase("Data Source=:memory:", DatabaseProvider.Sqlite));
    }
}
