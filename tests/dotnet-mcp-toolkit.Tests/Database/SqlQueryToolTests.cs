using System.Text.Json;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

public sealed class SqlQueryToolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlquery-" + Guid.NewGuid().ToString("N"));
    private readonly string _connectionString;

    public SqlQueryToolTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "test.db")}";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, price REAL, payload BLOB, created TEXT);
            INSERT INTO items (id, name, price, payload, created) VALUES (1, 'alpha', 1.5, X'0102', '2026-01-02T03:04:05Z');
            INSERT INTO items (id, name, price, payload, created) VALUES (2, NULL, 2.0, NULL, NULL);
            INSERT INTO items (id, name, price, payload, created) VALUES (3, 'gamma', 3.0, NULL, NULL);
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_SelectQuery_ReturnsColumnsAndRows()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT id, name FROM items WHERE id = 1"}"""));

        Assert.False(result.IsError);
        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(1, json.GetProperty("rowCount").GetInt32());
        Assert.False(json.GetProperty("truncated").GetBoolean());
        Assert.Equal("id", json.GetProperty("columns")[0].GetProperty("name").GetString());
        Assert.Equal("alpha", json.GetProperty("rows")[0][1].GetString());
    }

    [Fact]
    public async Task InvokeAsync_NullAndBlobAndDateValues_AreMapped()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT name, payload, created FROM items WHERE id = 1 OR id = 2 ORDER BY id"}"""));

        var rows = JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("rows");
        Assert.Equal(JsonValueKind.Null, rows[1][0].ValueKind);
        Assert.Equal("AQI=", rows[0][1].GetString());
        Assert.Equal("2026-01-02T03:04:05Z", rows[0][2].GetString());
    }

    [Fact]
    public async Task InvokeAsync_MoreRowsThanLimit_ReturnsTruncated()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT id FROM items", "maxRows": 2}"""));

        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(2, json.GetProperty("rowCount").GetInt32());
        Assert.True(json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_ParametersBoundByName_FilterRows()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT name FROM items WHERE id = @id AND name = @name", "parameters": {"id": 1, "name": "alpha"}}"""));

        var json = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(1, json.GetProperty("rowCount").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_WriteWhenReadOnly_ReturnsError()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "DELETE FROM items"}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_WriteWhenAllowed_ReturnsRecordsAffected()
    {
        var tool = CreateTool(allowWrites: true);

        var result = await tool.InvokeAsync(Args("""{"sql": "DELETE FROM items WHERE id > 1"}"""));

        Assert.False(result.IsError);
        Assert.Equal(2, JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("recordsAffected").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_SyntaxError_ReturnsError()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELEC * FROM items"}"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_MissingSql_ReturnsError()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_ObjectParameter_ReturnsError()
    {
        var tool = CreateTool(allowWrites: false);

        var result = await tool.InvokeAsync(Args("""{"sql": "SELECT @x", "parameters": {"x": {"nested": 1}}}"""));

        Assert.True(result.IsError);
    }

    private SqlQueryTool CreateTool(bool allowWrites) =>
        new(new SqliteConnectionFactory(_connectionString), allowWrites);

    private static JsonElement? Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
