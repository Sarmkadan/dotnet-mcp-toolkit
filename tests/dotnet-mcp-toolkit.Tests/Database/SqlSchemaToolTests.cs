using System.Data;
using System.Text.Json;
using DotnetMcpToolkit.Database;

namespace DotnetMcpToolkit.Tests.Database;

public sealed class SqlSchemaToolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlschema-" + Guid.NewGuid().ToString("N"));
    private readonly string _connectionString;

    public SqlSchemaToolTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "test.db")}";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE authors (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, bio TEXT DEFAULT 'none');
            CREATE TABLE books (
                id INTEGER PRIMARY KEY,
                author_id INTEGER NOT NULL REFERENCES authors(id),
                title TEXT,
                isbn TEXT UNIQUE
            );
            CREATE INDEX ix_books_title ON books (title, author_id);
            CREATE TABLE "we""ird" (id INTEGER PRIMARY KEY, value TEXT);
            CREATE VIEW book_titles AS SELECT title FROM books;
            """;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_NoArguments_ListsUserTablesAndViews()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);
        Assert.Equal(["authors", "book_titles", "books", "we\"ird"], Names(result));
    }

    [Fact]
    public async Task InvokeAsync_NoArguments_ReportsViewType()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(null);

        var view = Tables(result).EnumerateArray().Single(t => t.GetProperty("name").GetString() == "book_titles");
        Assert.Equal("view", view.GetProperty("type").GetString());
    }

    [Fact]
    public async Task InvokeAsync_TableArgument_ReturnsColumnsWithTypeNullabilityAndPrimaryKey()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "authors"}"""));

        var columns = OnlyTable(result).GetProperty("columns");
        Assert.Equal(3, columns.GetArrayLength());
        Assert.Equal("id", columns[0].GetProperty("name").GetString());
        Assert.Equal("INTEGER", columns[0].GetProperty("type").GetString());
        Assert.True(columns[0].GetProperty("primaryKey").GetBoolean());
        Assert.False(columns[1].GetProperty("nullable").GetBoolean());
        Assert.False(columns[1].GetProperty("primaryKey").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_ColumnWithDefault_ReturnsDefaultValue()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "authors"}"""));

        var bio = OnlyTable(result).GetProperty("columns")[2];
        Assert.Equal("'none'", bio.GetProperty("defaultValue").GetString());
    }

    [Fact]
    public async Task InvokeAsync_TableArgument_ReturnsForeignKeys()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "books"}"""));

        var foreignKey = OnlyTable(result).GetProperty("foreignKeys")[0];
        Assert.Equal("author_id", foreignKey.GetProperty("column").GetString());
        Assert.Equal("authors", foreignKey.GetProperty("refTable").GetString());
        Assert.Equal("id", foreignKey.GetProperty("refColumn").GetString());
    }

    [Fact]
    public async Task InvokeAsync_TableArgument_ReturnsNamedIndexWithColumnsInOrder()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "books"}"""));

        var index = OnlyTable(result).GetProperty("indexes").EnumerateArray()
            .Single(i => i.GetProperty("name").GetString() == "ix_books_title");
        Assert.False(index.GetProperty("unique").GetBoolean());
        Assert.Equal(["title", "author_id"], index.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!));
    }

    [Fact]
    public async Task InvokeAsync_TableArgument_OmitsInternalAutoIndexes()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "books"}"""));

        // isbn is UNIQUE, which SQLite backs with sqlite_autoindex_books_1; it is internal and hidden by default.
        Assert.Equal(["ix_books_title"], OnlyTable(result).GetProperty("indexes").EnumerateArray().Select(i => i.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task InvokeAsync_TableArgument_ReturnsOnlyThatTable()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "books"}"""));

        Assert.Equal(["books"], Names(result));
    }

    [Fact]
    public async Task InvokeAsync_TableNameWithQuote_ReturnsError()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "we\"ird"}"""));

        Assert.True(result.IsError);
        Assert.Contains("table must start", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_TableNameInjectionAttempt_ReturnsErrorAndLeavesSchemaIntact()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "x\" ); DROP TABLE books; --"}"""));

        Assert.True(result.IsError);
        Assert.Contains("table must start", result.Content[0].Text);

        var intact = await tool.InvokeAsync(Args("""{"table": "books"}"""));
        Assert.False(intact.IsError);
    }

    [Fact]
    public async Task InvokeAsync_UnknownTable_ReturnsError()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"table": "missing"}"""));

        Assert.True(result.IsError);
        Assert.Equal("Table 'missing' does not exist", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_DefaultOptions_ExcludesSqliteSequence()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(null);

        Assert.DoesNotContain("sqlite_sequence", Names(result));
    }

    [Fact]
    public async Task InvokeAsync_IncludeSystem_ListsSqliteSequence()
    {
        var tool = CreateTool();

        var result = await tool.InvokeAsync(Args("""{"includeSystem": true}"""));

        Assert.Contains("sqlite_sequence", Names(result));
    }

    [Fact]
    public async Task InvokeAsync_GenericProviderWithoutSchemaCollections_ReturnsError()
    {
        // Microsoft.Data.Sqlite does not define the Tables collection, so the generic path has nothing to read.
        var tool = new SqlSchemaTool(new ProviderOverrideConnectionFactory(DatabaseProvider.PostgreSql, _connectionString));

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
        Assert.Contains("does not expose", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_GenericProvider_MapsTablesViewsAndColumns()
    {
        var tool = new SqlSchemaTool(new FakeSchemaConnectionFactory(FakeCollection));

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);
        Assert.Equal(["authors", "book_titles"], Names(result));
        Assert.Equal("view", Tables(result).EnumerateArray().Last().GetProperty("type").GetString());

        var columns = OnlyTable(await tool.InvokeAsync(Args("""{"table": "authors"}"""))).GetProperty("columns");
        Assert.False(columns[0].GetProperty("nullable").GetBoolean());
        Assert.True(columns[1].GetProperty("nullable").GetBoolean());
        Assert.Equal("'x'", columns[1].GetProperty("defaultValue").GetString());
        Assert.False(columns[0].GetProperty("primaryKey").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_GenericProviderSystemSchema_SkippedUnlessIncluded()
    {
        var tool = new SqlSchemaTool(new FakeSchemaConnectionFactory(FakeCollection));

        var defaults = await tool.InvokeAsync(null);
        var included = await tool.InvokeAsync(Args("""{"includeSystem": true}"""));

        Assert.DoesNotContain("pg_stat_activity", Names(defaults));
        Assert.Contains("pg_stat_activity", Names(included));
    }

    [Fact]
    public async Task InvokeAsync_GenericProviderTableFilter_MatchesIgnoringCase()
    {
        var tool = new SqlSchemaTool(new FakeSchemaConnectionFactory(FakeCollection));

        var result = await tool.InvokeAsync(Args("""{"table": "AUTHORS"}"""));

        Assert.Equal(["authors"], Names(result));
    }

    private SqlSchemaTool CreateTool() =>
        new(new SqliteConnectionFactory(_connectionString));

    private static DataTable FakeCollection(string collectionName)
    {
        var table = new DataTable();

        switch (collectionName)
        {
            case "Tables":
                AddColumns(table, "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE");
                AddRow(table, "public", "authors", "BASE TABLE");
                AddRow(table, "public", "book_titles", "VIEW");
                AddRow(table, "pg_catalog", "pg_stat_activity", "VIEW");
                break;

            case "Columns":
                AddColumns(table, "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "DATA_TYPE", "IS_NULLABLE", "COLUMN_DEFAULT");
                AddRow(table, "public", "authors", "id", "integer", "NO", DBNull.Value);
                AddRow(table, "public", "authors", "name", "text", "YES", "'x'");
                break;
        }

        return table;
    }

    private static void AddColumns(DataTable table, params string[] names)
    {
        foreach (var name in names)
        {
            table.Columns.Add(name, typeof(object));
        }
    }

    private static void AddRow(DataTable table, params object[] values) => table.Rows.Add(values);

    private static string[] Names(McpToolResult result) =>
        Tables(result).EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray();

    private static JsonElement Tables(McpToolResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("tables");

    private static JsonElement OnlyTable(McpToolResult result) =>
        Tables(result).EnumerateArray().Single();

    private static JsonElement? Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
