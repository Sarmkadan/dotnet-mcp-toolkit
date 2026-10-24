using System.Text.RegularExpressions;

namespace DotnetMcpToolkit.Database;

/// <summary>
/// Input patterns shared by the database tools. Anchors use <c>\z</c>, so a trailing newline never matches.
/// </summary>
internal static partial class DatabasePatterns
{
    /// <summary>
    /// Table or view name accepted by <c>sql_schema</c>: identifiers with spaces, dots, dashes and dollar signs.
    /// Quotes and semicolons are rejected outright.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_$ .-]{0,127}\z")]
    internal static partial Regex TableName();

    /// <summary>
    /// Named parameter accepted by <c>sql_query</c>, written without the leading <c>@</c>.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}\z")]
    internal static partial Regex ParameterName();
}
