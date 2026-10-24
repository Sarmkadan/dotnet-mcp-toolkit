using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// MCP tool <c>sql_explain</c>: returns the execution plan of one read-only SQL statement without running it.
/// The statement is always checked in read-only mode, and no <c>ANALYZE</c> variant is issued, so the plan
/// request never executes the statement. The plan is returned as tab-separated rows joined by newlines.
/// </summary>
public sealed class SqlExplainTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["sql"],
            "properties": {
                "sql": {
                    "type": "string",
                    "description": "A single read-only SQL statement to explain"
                }
            }
        }
        """).RootElement.Clone();

    private const int _defaultTimeoutSeconds = 30;

    private readonly IDbConnectionFactory _factory;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public SqlExplainTool(IDbConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public string Name => "sql_explain";

    /// <inheritdoc />
    public string Description => "Returns the execution plan of a single read-only SQL statement without running it.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var sql = ToolArguments.GetString(arguments, "sql");

        if (string.IsNullOrWhiteSpace(sql))
        {
            return McpToolResult.Error("Missing required argument: sql");
        }

        try
        {
            SqlStatementGuard.EnsureAllowed(sql, allowWrites: false);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResult.Error(ex.Message);
        }

        var planStatement = _factory.Provider switch
        {
            DatabaseProvider.Sqlite => "EXPLAIN QUERY PLAN " + sql,
            DatabaseProvider.PostgreSql => "EXPLAIN (FORMAT TEXT) " + sql,
            DatabaseProvider.MySql => "EXPLAIN " + sql,
            _ => null,
        };

        if (planStatement is null)
        {
            return McpToolResult.Error($"Execution plans are not supported for {_factory.Provider}");
        }

        try
        {
            await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            command.CommandText = planStatement;
            command.CommandTimeout = _defaultTimeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var lines = new List<string>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(ReadRow(reader));
            }

            return McpToolResult.Text(string.Join('\n', lines));
        }
        catch (DbException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }

    private static string ReadRow(DbDataReader reader)
    {
        var values = new string[reader.FieldCount];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Join('\t', values);
    }
}
