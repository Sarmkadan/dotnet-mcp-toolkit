using System.Text.Json;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

// A temp file rather than ":memory:", because every SQLite connection to ":memory:" gets its own empty database.
public sealed class SqlToolsGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlguard-" + Guid.NewGuid().ToString("N"));
    private readonly string _connectionString;

    public SqlToolsGuardTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "guard.db")}";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO items (id, name) VALUES (1, 'alpha');
            INSERT INTO items (id, name) VALUES (2, 'beta');
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task SqlQuery_MissingSql_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"maxRows": 5}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SqlQuery_SqlAsNumber_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": 42}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SqlQuery_MaxRowsZero_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT id FROM items", "maxRows": 0}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SqlQuery_MaxRowsAboveLimit_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT id FROM items", "maxRows": 10000}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SqlQuery_ParameterNameStartsWithDigit_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT name FROM items", "parameters": {"1bad": 1}}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task SqlQuery_ParameterValueIsObject_ReturnsError()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT name FROM items WHERE id = @id", "parameters": {"id": {"nested": 1}}}"""));

        Assert.True(result.IsError);
        Assert.Equal("parameter 'id' must be a scalar", result.Content[0].Text);
    }

    [Fact]
    public async Task SqlQuery_DropTableWithoutWrites_ReturnsErrorAndKeepsTable()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "DROP TABLE items"}"""));

        Assert.True(result.IsError);
        Assert.Equal("2", CountItems());
    }

    [Fact]
    public async Task SqlQuery_ValidParameterizedSelect_ReturnsRows()
    {
        var tool = CreateQueryTool();

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT name FROM items WHERE id = @id", "parameters": {"id": 1}}"""));

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(1, json.GetProperty("rowCount").GetInt32());
        Assert.Equal("alpha", json.GetProperty("rows")[0][0].GetString());
    }

    [Fact]
    public async Task SqlSchema_TableNameWithInjection_ReturnsErrorAndKeepsTable()
    {
        var tool = new SqlSchemaTool(new SqliteConnectionFactory(_connectionString));

        var result = await tool.InvokeAsync(Args("""{"table": "items; DROP TABLE items"}"""));

        Assert.True(result.IsError);
        Assert.Equal("2", CountItems());
    }

    [Fact]
    public async Task SqlExplain_InsertStatement_ReturnsErrorAndKeepsRows()
    {
        var tool = new SqlExplainTool(new SqliteConnectionFactory(_connectionString));

        var result = await tool.InvokeAsync(Args("""{"sql": "INSERT INTO items (id, name) VALUES (3, 'gamma')"}"""));

        Assert.True(result.IsError);
        Assert.Equal("2", CountItems());
    }

    [Fact]
    public void SqlQueryTool_NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlQueryTool(null!, allowWrites: false));
    }

    [Fact]
    public void SqlSchemaTool_NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlSchemaTool(null!));
    }

    [Fact]
    public void SqlExplainTool_NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlExplainTool(null!));
    }

    [Fact]
    public void SqlQueryTool_MaxRowsZero_ThrowsArgumentOutOfRangeException()
    {
        var factory = new SqliteConnectionFactory(_connectionString);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlQueryTool(factory, allowWrites: false, maxRows: 0));
    }

    [Fact]
    public void SqliteConnectionFactory_WhitespaceConnectionString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SqliteConnectionFactory("   "));
    }

    private SqlQueryTool CreateQueryTool() =>
        new(new SqliteConnectionFactory(_connectionString), allowWrites: false);

    private string CountItems()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items";
        return command.ExecuteScalar()!.ToString()!;
    }

    private static JsonElement? Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
