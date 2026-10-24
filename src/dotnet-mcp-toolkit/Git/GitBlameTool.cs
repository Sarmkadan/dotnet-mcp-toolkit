using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// MCP tool <c>git_blame</c>: attributes each line of a file to the commit that last changed it.
/// </summary>
public sealed class GitBlameTool : GitToolBase
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["path"],
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Path of the file to blame"
                },
                "startLine": {
                    "type": "integer",
                    "description": "First line to blame, 1-based (default 1)"
                },
                "endLine": {
                    "type": "integer",
                    "description": "Last line to blame, 1-based inclusive (default end of file)"
                }
            }
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the repository to inspect.</param>
    public GitBlameTool(GitCommandRunner runner)
        : base(runner, "git_blame", "Shows, for each line of a file, the commit, author and summary that last changed it.", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? gitArguments,
        [NotNullWhen(false)] out string? message)
    {
        if (ToolArguments.GetString(arguments, "path") is not { Length: > 0 } path)
        {
            gitArguments = null;
            message = "Missing required argument: path";

            return false;
        }

        var startLine = ToolArguments.GetInt32(arguments, "startLine");
        var endLine = ToolArguments.GetInt32(arguments, "endLine");

        if (startLine is < 1 || endLine is < 1)
        {
            gitArguments = null;
            message = "startLine and endLine must be 1 or greater";

            return false;
        }

        if (startLine > endLine)
        {
            gitArguments = null;
            message = "startLine must not be greater than endLine";

            return false;
        }

        var args = new List<string> { "blame", "--porcelain" };

        if (startLine is not null || endLine is not null)
        {
            var start = (startLine ?? 1).ToString(CultureInfo.InvariantCulture);
            var end = endLine?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            args.Add("-L");
            args.Add($"{start},{end}");
        }

        args.Add("--");
        args.Add(path);

        gitArguments = args;
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(string standardOutput)
    {
        return McpToolResult.Json(ParseBlame(standardOutput));
    }

    // Porcelain output: each block starts with "<sha> <orig-line> <final-line> [<count>]". Author and summary
    // headers are printed only the first time a commit appears, and the source line follows a TAB.
    private static List<object> ParseBlame(string output)
    {
        var identities = new Dictionary<string, BlameIdentity>(StringComparer.Ordinal);
        var lines = new List<object>();
        var sha = string.Empty;
        var lineNumber = 0;

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith('\t'))
            {
                var identity = identities.GetValueOrDefault(sha, BlameIdentity.Empty);
                lines.Add(new { line = lineNumber, sha, author = identity.Author, summary = identity.Summary, text = line[1..] });
            }
            else if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                identities[sha] = identities.GetValueOrDefault(sha, BlameIdentity.Empty) with { Author = line["author ".Length..] };
            }
            else if (line.StartsWith("summary ", StringComparison.Ordinal))
            {
                identities[sha] = identities.GetValueOrDefault(sha, BlameIdentity.Empty) with { Summary = line["summary ".Length..] };
            }
            else if (TryParseHeader(line, out var headerSha, out var finalLine))
            {
                sha = headerSha;
                lineNumber = finalLine;
            }
        }

        return lines;
    }

    private static bool TryParseHeader(string line, out string sha, out int finalLine)
    {
        var parts = line.Split(' ');

        if (parts.Length >= 3
            && parts[0].Length is 40 or 64
            && parts[0].All(Uri.IsHexDigit)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out finalLine))
        {
            sha = parts[0];

            return true;
        }

        sha = string.Empty;
        finalLine = 0;

        return false;
    }

    private sealed record BlameIdentity(string Author, string Summary)
    {
        public static BlameIdentity Empty { get; } = new(string.Empty, string.Empty);
    }
}
