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

    private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _maxCommandTimeout = TimeSpan.FromMinutes(10);

    private readonly IDbConnectionFactory _factory;
    private readonly int _commandTimeoutSeconds;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <param name="commandTimeout">Time limit for the plan command, above zero and at most 10 minutes; <see langword="null"/> means 30 seconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commandTimeout"/> is not in (0, 10 minutes].</exception>
    public SqlExplainTool(IDbConnectionFactory factory, TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var timeout = commandTimeout ?? _defaultCommandTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, _maxCommandTimeout);

        _factory = factory;
        _commandTimeoutSeconds = (int)Math.Ceiling(timeout.TotalSeconds);
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
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentsError)
        {
            return argumentsError;
        }

        var sql = ToolArguments.GetString(arguments, "sql");

        if (string.IsNullOrWhiteSpace(sql))
        {
            return McpToolResult.Error("Missing required argument: sql");
        }

        // EnsureAllowed only passes statements that classify as Read, so Write, Ddl and Unknown fail here,
        // before any connection is opened.
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
            command.CommandTimeout = _commandTimeoutSeconds;

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
        catch (TimeoutException ex)
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
