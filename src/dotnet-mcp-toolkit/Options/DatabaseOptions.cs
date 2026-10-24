namespace DotnetMcpToolkit.Options;

/// <summary>
/// Configuration for the database tool pack. Binds from a configuration section such as <c>Mcp:Database</c>.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>
    /// Gets or sets the ADO.NET connection string. Required.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Gets or sets the database engine. Defaults to <see cref="DatabaseProvider.Sqlite"/>.
    /// </summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// Gets or sets a value indicating whether writes and DDL are permitted. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowWrites { get; set; }

    /// <summary>
    /// Gets or sets the default row limit of <c>sql_query</c>, from 1 to 5000. Defaults to 500.
    /// </summary>
    public int MaxRows { get; set; } = 500;

    /// <summary>
    /// Gets or sets the time limit for each command in seconds, from 1 to 600. Applies to <c>sql_query</c> and <c>sql_explain</c>. Defaults to 30.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Checks the options.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="ConnectionString"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="Provider"/> is not defined, <see cref="MaxRows"/> is outside 1-5000, or <see cref="CommandTimeoutSeconds"/> is outside 1-600.
    /// </exception>
    public void Validate()
    {
        OptionsValidation.RequireText(ConnectionString, nameof(ConnectionString));
        OptionsValidation.RequireDefined(Provider, nameof(Provider));
        OptionsValidation.RequireInRange(MaxRows, 1, 5000, nameof(MaxRows));
        OptionsValidation.RequireInRange(CommandTimeoutSeconds, 1, 600, nameof(CommandTimeoutSeconds));
    }
}
