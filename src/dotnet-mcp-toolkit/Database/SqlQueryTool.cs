using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

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
    private const int _defaultTimeoutSeconds = 30;

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
    /// <param name="commandTimeout">Time limit for each command; <see langword="null"/> means 30 seconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRows"/> is outside 1-5000, or <paramref name="commandTimeout"/> is not positive.</exception>
    public SqlQueryTool(IDbConnectionFactory factory, bool allowWrites, int maxRows = 500, TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, _maxRowsLimit);

        var timeout = commandTimeout ?? TimeSpan.FromSeconds(_defaultTimeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

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
        var sql = ToolArguments.GetString(arguments, "sql");

        if (string.IsNullOrWhiteSpace(sql))
        {
            return McpToolResult.Error("Missing required argument: sql");
        }

        var maxRows = ToolArguments.GetInt32(arguments, "maxRows") ?? _maxRows;

        if (maxRows < 1 || maxRows > _maxRowsLimit)
        {
            return McpToolResult.Error($"maxRows must be between 1 and {_maxRowsLimit}");
        }

        var (bindings, bindingError) = ParseParameters(arguments);

        if (bindingError is not null)
        {
            return McpToolResult.Error(bindingError);
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

    private static (List<KeyValuePair<string, object>> Bindings, string? Error) ParseParameters(JsonElement? arguments)
    {
        var bindings = new List<KeyValuePair<string, object>>();

        if (arguments is not { ValueKind: JsonValueKind.Object } args ||
            !args.TryGetProperty("parameters", out var parameters))
        {
            return (bindings, null);
        }

        if (parameters.ValueKind == JsonValueKind.Null)
        {
            return (bindings, null);
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return ([], "parameters must be an object");
        }

        foreach (var property in parameters.EnumerateObject())
        {
            var value = ToParameterValue(property.Value);

            if (value is null)
            {
                return ([], $"Parameter '{property.Name}' must be a string, number, boolean or null");
            }

            var name = property.Name.StartsWith('@') ? property.Name : "@" + property.Name;
            bindings.Add(new KeyValuePair<string, object>(name, value));
        }

        return (bindings, null);
    }

    private static object? ToParameterValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    return integer;
                }

                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return DBNull.Value;

            default:
                return null;
        }
    }
}
