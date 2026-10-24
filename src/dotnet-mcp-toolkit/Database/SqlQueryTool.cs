using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// MCP tool <c>sql_query</c>: runs one SQL statement through an <see cref="IDbConnectionFactory"/>.
/// Read-only unless constructed with <c>allowWrites</c>; the statement is checked by <see cref="SqlStatementGuard"/> before it runs.
/// </summary>
public sealed class SqlQueryTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["sql"],
            "properties": {
                "sql": {
                    "type": "string",
                    "description": "The SQL statement to run. Use @name placeholders for values passed in parameters"
                },
                "maxRows": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 5000,
                    "description": "Maximum number of rows to return; defaults to the server setting"
                },
                "parameters": {
                    "type": "object",
                    "description": "Values bound to @name placeholders in sql",
                    "additionalProperties": {
                        "type": ["string", "number", "boolean", "null"]
                    }
                }
            }
        }
        """).RootElement.Clone();

    private const int _maxRowsLimit = 5000;
    private const int _maxSqlLength = 100_000;
    private const int _defaultTimeoutSeconds = 30;
    private static readonly TimeSpan _maxCommandTimeout = TimeSpan.FromMinutes(10);

    private readonly IDbConnectionFactory _factory;
    private readonly bool _allowWrites;
    private readonly int _maxRows;
    private readonly TimeSpan _commandTimeout;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <param name="allowWrites">When <see langword="false"/>, only single read-only statements are executed.</param>
    /// <param name="maxRows">Default row limit for result sets, between 1 and 5000.</param>
    /// <param name="commandTimeout">Time limit for each command, above zero and at most 10 minutes; <see langword="null"/> means 30 seconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRows"/> is outside 1-5000, or <paramref name="commandTimeout"/> is not in (0, 10 minutes].</exception>
    public SqlQueryTool(IDbConnectionFactory factory, bool allowWrites, int maxRows = 500, TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, _maxRowsLimit);

        var timeout = commandTimeout ?? TimeSpan.FromSeconds(_defaultTimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, _maxCommandTimeout);

        _factory = factory;
        _allowWrites = allowWrites;
        _maxRows = maxRows;
        _commandTimeout = timeout;
    }

    /// <inheritdoc />
    public string Name => "sql_query";

    /// <inheritdoc />
    public string Description => "Runs a single SQL statement and returns the result rows as JSON. Read-only unless the server allows writes.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentsError)
        {
            return argumentsError;
        }

        if (ToolArgumentValidation.RequireString(arguments, "sql", out var sql) is { } sqlError)
        {
            return sqlError;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            return McpToolResult.Error("Argument 'sql' must not be blank");
        }

        if (ToolArgumentValidation.RequireMaxLength(sql, "sql", _maxSqlLength) is { } lengthError)
        {
            return lengthError;
        }

        if (ToolArgumentValidation.OptionalInt32(arguments, "maxRows", 1, _maxRowsLimit, _maxRows, out var maxRows) is { } maxRowsError)
        {
            return maxRowsError;
        }

        var (bindings, bindingError) = ParseParameters(arguments);

        if (bindingError is not null)
        {
            return bindingError;
        }

        try
        {
            SqlStatementGuard.EnsureAllowed(sql, _allowWrites);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResult.Error(ex.Message);
        }

        try
        {
            await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            command.CommandText = sql;
            command.CommandTimeout = (int)Math.Ceiling(_commandTimeout.TotalSeconds);

            foreach (var (name, value) in bindings)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (reader.FieldCount == 0)
            {
                await reader.CloseAsync().ConfigureAwait(false);
                return McpToolResult.Json(new { recordsAffected = reader.RecordsAffected });
            }

            return await ReadResultAsync(reader, maxRows, cancellationToken).ConfigureAwait(false);
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

    private static async Task<McpToolResult> ReadResultAsync(DbDataReader reader, int maxRows, CancellationToken cancellationToken)
    {
        var columns = new List<object>(reader.FieldCount);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new { name = reader.GetName(i), type = reader.GetDataTypeName(i) });
        }

        var rows = new List<object?[]>();
        var truncated = false;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count == maxRows)
            {
                truncated = true;
                break;
            }

            rows.Add(ReadRow(reader));
        }

        return McpToolResult.Json(new { columns, rows, rowCount = rows.Count, truncated });
    }

    private static object?[] ReadRow(DbDataReader reader)
    {
        var values = new object?[reader.FieldCount];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.IsDBNull(i) ? null : MapValue(reader.GetValue(i));
        }

        return values;
    }

    private static object? MapValue(object value)
    {
        return value switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private static (List<KeyValuePair<string, object>> Bindings, McpToolResult? Error) ParseParameters(JsonElement? arguments)
    {
        var bindings = new List<KeyValuePair<string, object>>();

        // A JSON null counts as absent, the same as for the other optional arguments.
        if (!ToolArgumentValidation.HasArgument(arguments, "parameters"))
        {
            return (bindings, null);
        }

        var parameters = arguments!.Value.GetProperty("parameters");

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return ([], McpToolResult.Error("parameters must be a JSON object"));
        }

        foreach (var property in parameters.EnumerateObject())
        {
            if (!DatabasePatterns.ParameterName().IsMatch(property.Name))
            {
                return ([], McpToolResult.Error($"parameter name '{property.Name}' is invalid: use 1-64 letters, digits or underscores, starting with a letter or underscore"));
            }

            if (!TryGetScalar(property.Value, out var value))
            {
                return ([], McpToolResult.Error($"parameter '{property.Name}' must be a scalar"));
            }

            // Names go into Parameters, never into the SQL text.
            bindings.Add(new KeyValuePair<string, object>("@" + property.Name, value));
        }

        return (bindings, null);
    }

    private static bool TryGetScalar(JsonElement element, out object value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString()!;
                return true;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    value = integer;
                    return true;
                }

                value = element.GetDouble();
                return true;

            case JsonValueKind.True:
                value = true;
                return true;

            case JsonValueKind.False:
                value = false;
                return true;

            case JsonValueKind.Null:
                value = DBNull.Value;
                return true;

            default:
                value = DBNull.Value;
                return false;
        }
    }
}
