namespace DotnetMcpToolkit;

/// <summary>
/// Database engines supported by the database tool pack.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>SQLite via Microsoft.Data.Sqlite.</summary>
    Sqlite,

    /// <summary>PostgreSQL via Npgsql.</summary>
    PostgreSql,

    /// <summary>SQL Server via Microsoft.Data.SqlClient.</summary>
    SqlServer,

    /// <summary>MySQL / MariaDB via MySqlConnector.</summary>
    MySql,
}
