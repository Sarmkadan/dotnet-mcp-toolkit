namespace DotnetMcpToolkit.Database;

/// <summary>
/// The broad category of a SQL statement, as determined by <see cref="SqlStatementGuard.Classify"/>.
/// </summary>
public enum SqlStatementKind
{
    /// <summary>A query that only reads data: SELECT, WITH ... SELECT, EXPLAIN, SHOW, DESCRIBE, PRAGMA without assignment, VALUES.</summary>
    Read,

    /// <summary>A statement that modifies data: INSERT, UPDATE, DELETE, MERGE, TRUNCATE, COPY, CALL, EXEC and similar.</summary>
    Write,

    /// <summary>A statement that changes schema or privileges: CREATE, ALTER, DROP, GRANT, REVOKE, ATTACH, DETACH, VACUUM.</summary>
    Ddl,

    /// <summary>A statement whose kind cannot be determined.</summary>
    Unknown,
}
