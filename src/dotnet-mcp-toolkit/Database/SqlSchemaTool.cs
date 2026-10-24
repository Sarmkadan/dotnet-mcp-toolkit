using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// MCP tool <c>sql_schema</c>: describes tables and views (columns, primary keys, foreign keys, indexes)
/// through an <see cref="IDbConnectionFactory"/>. SQLite is introspected through its catalog and <c>PRAGMA</c>
/// statements. Other providers use <see cref="DbConnection.GetSchemaAsync(string, CancellationToken)"/>, which
/// reports tables and columns only, so their <c>foreignKeys</c> and <c>indexes</c> are always empty.
/// </summary>
public sealed class SqlSchemaTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "table": {
                    "type": "string",
                    "description": "Name of one table or view to describe; omit to describe all of them"
                },
                "includeSystem": {
                    "type": "boolean",
                    "description": "Include system tables and internal indexes; defaults to false"
                }
            }
        }
        """).RootElement.Clone();

    // Schemas that hold catalog objects rather than user data. The generic path skips them unless includeSystem is set.
    private static readonly HashSet<string> _systemSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        "information_schema", "pg_catalog", "sys",
    };

    private readonly IDbConnectionFactory _factory;

    /// <summary>
    /// Creates the tool.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public SqlSchemaTool(IDbConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public string Name => "sql_schema";

    /// <inheritdoc />
    public string Description => "Describes tables and views: columns, primary keys, foreign keys and indexes. Pass table to describe one object.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentsError)
        {
            return argumentsError;
        }

        string? table = null;

        if (ToolArgumentValidation.HasArgument(arguments, "table"))
        {
            if (ToolArgumentValidation.RequireString(arguments, "table", out var tableName) is { } tableError)
            {
                return tableError;
            }

            // The pattern keeps the name to plain identifiers; the PRAGMA statements still quote it as a second layer.
            if (!DatabasePatterns.TableName().IsMatch(tableName))
            {
                return McpToolResult.Error("table must start with a letter or underscore and contain only letters, digits, underscores, spaces, dots, dashes or dollar signs (at most 128 characters)");
            }

            table = tableName;
        }

        if (ToolArgumentValidation.OptionalBoolean(arguments, "includeSystem", defaultValue: false, out var includeSystem) is { } includeError)
        {
            return includeError;
        }

        try
        {
            await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);

            var tables = _factory.Provider switch
            {
                DatabaseProvider.Sqlite => await ReadSqliteTablesAsync(connection, table, includeSystem, cancellationToken).ConfigureAwait(false),
                _ => await ReadGenericTablesAsync(connection, table, includeSystem, cancellationToken).ConfigureAwait(false),
            };

            if (table is not null && tables.Count == 0)
            {
                return McpToolResult.Error($"Table '{table}' does not exist");
            }

            return McpToolResult.Json(new { tables });
        }
        catch (DbException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
        catch (TimeoutException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return McpToolResult.Error(ex.Message);
        }
    }

    private static async Task<List<TableDescription>> ReadSqliteTablesAsync(
        DbConnection connection, string? table, bool includeSystem, CancellationToken cancellationToken)
    {
        var entries = new List<(string Name, string Type)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name, type FROM sqlite_master
                WHERE type IN ('table', 'view')
                  AND (@includeSystem = 1 OR substr(name, 1, 7) <> 'sqlite_')
                  AND (@table IS NULL OR name = @table)
                ORDER BY name
                """;

            AddParameter(command, "@includeSystem", includeSystem ? 1 : 0);
            AddParameter(command, "@table", (object?)table ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var tables = new List<TableDescription>(entries.Count);

        foreach (var (name, type) in entries)
        {
            var quoted = QuoteIdentifier(name);
            var columns = await ReadSqliteColumnsAsync(connection, quoted, cancellationToken).ConfigureAwait(false);
            var foreignKeys = await ReadSqliteForeignKeysAsync(connection, quoted, cancellationToken).ConfigureAwait(false);
            var indexes = await ReadSqliteIndexesAsync(connection, quoted, includeSystem, cancellationToken).ConfigureAwait(false);

            tables.Add(new TableDescription(name, type, columns, foreignKeys, indexes));
        }

        return tables;
    }

    private static async Task<List<ColumnDescription>> ReadSqliteColumnsAsync(
        DbConnection connection, string quotedTable, CancellationToken cancellationToken)
    {
        var columns = new List<ColumnDescription>();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({quotedTable})";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var nameOrdinal = reader.GetOrdinal("name");
        var typeOrdinal = reader.GetOrdinal("type");
        var notNullOrdinal = reader.GetOrdinal("notnull");
        var defaultOrdinal = reader.GetOrdinal("dflt_value");
        var pkOrdinal = reader.GetOrdinal("pk");

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new ColumnDescription(
                Name: reader.GetString(nameOrdinal),
                Type: reader.GetString(typeOrdinal),
                Nullable: reader.GetInt64(notNullOrdinal) == 0,
                PrimaryKey: reader.GetInt64(pkOrdinal) > 0,
                DefaultValue: reader.IsDBNull(defaultOrdinal) ? null : reader.GetString(defaultOrdinal)));
        }

        return columns;
    }

    private static async Task<List<ForeignKeyDescription>> ReadSqliteForeignKeysAsync(
        DbConnection connection, string quotedTable, CancellationToken cancellationToken)
    {
        var foreignKeys = new List<ForeignKeyDescription>();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({quotedTable})";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var fromOrdinal = reader.GetOrdinal("from");
        var tableOrdinal = reader.GetOrdinal("table");
        var toOrdinal = reader.GetOrdinal("to");

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // "to" is NULL when the foreign key targets the parent's primary key implicitly.
            foreignKeys.Add(new ForeignKeyDescription(
                Column: reader.GetString(fromOrdinal),
                RefTable: reader.GetString(tableOrdinal),
                RefColumn: reader.IsDBNull(toOrdinal) ? null : reader.GetString(toOrdinal)));
        }

        return foreignKeys;
    }

    private static async Task<List<IndexDescription>> ReadSqliteIndexesAsync(
        DbConnection connection, string quotedTable, bool includeSystem, CancellationToken cancellationToken)
    {
        var indexes = new List<(string Name, bool Unique)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({quotedTable})";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var nameOrdinal = reader.GetOrdinal("name");
            var uniqueOrdinal = reader.GetOrdinal("unique");

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(nameOrdinal);

                // Automatic indexes (sqlite_autoindex_*) back PRIMARY KEY and UNIQUE constraints; they are internal.
                if (!includeSystem && name.StartsWith("sqlite_", StringComparison.Ordinal))
                {
                    continue;
                }

                indexes.Add((name, reader.GetInt64(uniqueOrdinal) != 0));
            }
        }

        var result = new List<IndexDescription>(indexes.Count);

        foreach (var (name, unique) in indexes)
        {
            var columns = new List<string>();

            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info({QuoteIdentifier(name)})";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var columnOrdinal = reader.GetOrdinal("name");

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Expression indexes have no column name; skip them rather than invent one.
                if (!reader.IsDBNull(columnOrdinal))
                {
                    columns.Add(reader.GetString(columnOrdinal));
                }
            }

            result.Add(new IndexDescription(name, unique, columns));
        }

        return result;
    }

    private static async Task<List<TableDescription>> ReadGenericTablesAsync(
        DbConnection connection, string? table, bool includeSystem, CancellationToken cancellationToken)
    {
        DataTable tableRows;
        DataTable columnRows;

        try
        {
            tableRows = await connection.GetSchemaAsync("Tables", cancellationToken).ConfigureAwait(false);
            columnRows = await connection.GetSchemaAsync("Columns", cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // ADO.NET reports an undefined schema collection with ArgumentException; surface it as "unsupported" instead.
            throw new NotSupportedException($"The provider does not expose the Tables and Columns schema collections: {ex.Message}", ex);
        }

        var columnsByTable = new Dictionary<(string? Schema, string Name), List<ColumnDescription>>();

        foreach (DataRow row in columnRows.Rows)
        {
            var schema = ReadString(row, "TABLE_SCHEMA");
            var name = ReadString(row, "TABLE_NAME");

            if (name is null || IsFilteredOut(schema, includeSystem))
            {
                continue;
            }

            var key = (schema, name);

            if (!columnsByTable.TryGetValue(key, out var columns))
            {
                columns = [];
                columnsByTable[key] = columns;
            }

            columns.Add(new ColumnDescription(
                Name: ReadString(row, "COLUMN_NAME") ?? string.Empty,
                Type: ReadString(row, "DATA_TYPE") ?? string.Empty,
                Nullable: ReadBoolean(row, "IS_NULLABLE", defaultValue: true),
                PrimaryKey: false,
                DefaultValue: ReadString(row, "COLUMN_DEFAULT")));
        }

        var tables = new List<TableDescription>();

        foreach (DataRow row in tableRows.Rows)
        {
            var schema = ReadString(row, "TABLE_SCHEMA");
            var name = ReadString(row, "TABLE_NAME");

            if (name is null || IsFilteredOut(schema, includeSystem))
            {
                continue;
            }

            if (table is not null && !string.Equals(name, table, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var type = ReadString(row, "TABLE_TYPE") ?? string.Empty;
            var isView = type.Contains("VIEW", StringComparison.OrdinalIgnoreCase);

            columnsByTable.TryGetValue((schema, name), out var columns);

            tables.Add(new TableDescription(
                Name: name,
                Type: isView ? "view" : "table",
                Columns: columns ?? [],
                ForeignKeys: [],
                Indexes: []));
        }

        return tables;
    }

    private static bool IsFilteredOut(string? schema, bool includeSystem) =>
        !includeSystem && schema is not null && _systemSchemas.Contains(schema);

    private static string? ReadString(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] is DBNull or null)
        {
            return null;
        }

        return Convert.ToString(row[column], CultureInfo.InvariantCulture);
    }

    private static bool ReadBoolean(DataRow row, string column, bool defaultValue)
    {
        if (!row.Table.Columns.Contains(column) || row[column] is DBNull or null)
        {
            return defaultValue;
        }

        return row[column] switch
        {
            bool flag => flag,
            string text => text.Equals("YES", StringComparison.OrdinalIgnoreCase) || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
            _ => defaultValue,
        };
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Quotes an identifier for SQLite: wraps it in double quotes and doubles any embedded quote.
    /// </summary>
    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed record TableDescription(
        string Name,
        string Type,
        List<ColumnDescription> Columns,
        List<ForeignKeyDescription> ForeignKeys,
        List<IndexDescription> Indexes);

    private sealed record ColumnDescription(
        string Name,
        string Type,
        bool Nullable,
        bool PrimaryKey,
        string? DefaultValue);

    private sealed record ForeignKeyDescription(string Column, string RefTable, string? RefColumn);

    private sealed record IndexDescription(string Name, bool Unique, List<string> Columns);
}
