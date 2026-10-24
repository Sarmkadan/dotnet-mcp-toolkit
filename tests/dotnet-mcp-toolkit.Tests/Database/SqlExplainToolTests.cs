using System.Text.Json;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

public sealed class SqlExplainToolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlexplain-" + Guid.NewGuid().ToString("N"));
    private readonly string _connectionString;

    public SqlExplainToolTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "test.db")}";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO items (id, name) VALUES (1, 'alpha');
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_SelectStatement_ReturnsPlanMentioningTable()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT * FROM items WHERE id = 1"}"""));

        Assert.False(result.IsError);
        Assert.Contains("items", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_SelectStatement_DoesNotChangeRows()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        await tool.InvokeAsync(Args("""{"sql": "SELECT * FROM items"}"""));

        Assert.Equal("1", CountRows());
    }

    [Fact]
    public async Task InvokeAsync_WriteStatement_ReturnsErrorWithoutRunningIt()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        var result = await tool.InvokeAsync(Args("""{"sql": "DELETE FROM items"}"""));

        Assert.True(result.IsError);
        Assert.Contains("Writes are not allowed", result.Content[0].Text);
        Assert.Equal("1", CountRows());
    }

    [Fact]
    public async Task InvokeAsync_MultipleStatements_ReturnsError()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT 1; DELETE FROM items"}"""));

        Assert.True(result.IsError);
        Assert.Equal("1", CountRows());
    }

    [Fact]
    public async Task InvokeAsync_InvalidSql_ReturnsDatabaseError()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT * FROM missing_table"}"""));

        Assert.True(result.IsError);
        Assert.Contains("missing_table", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_MissingSql_ReturnsError()
    {
        var tool = CreateTool(DatabaseProvider.Sqlite);

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
        Assert.Equal("Missing required argument: sql", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_SqlServerProvider_ReturnsNotSupportedError()
    {
        var tool = CreateTool(DatabaseProvider.SqlServer);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT 1"}"""));

        Assert.True(result.IsError);
        Assert.Equal("Execution plans are not supported for SqlServer", result.Content[0].Text);
    }

    private SqlExplainTool CreateTool(DatabaseProvider provider) =>
        new(new ProviderOverrideConnectionFactory(provider, _connectionString));

    private string CountRows()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items";
        return command.ExecuteScalar()!.ToString()!;
    }

    private static JsonElement? Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
