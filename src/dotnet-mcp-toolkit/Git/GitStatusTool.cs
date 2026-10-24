using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// MCP tool <c>git_status</c>: reports the current branch, its distance from the upstream and the changed files.
/// </summary>
public sealed class GitStatusTool : GitToolBase
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the repository to inspect.</param>
    public GitStatusTool(GitCommandRunner runner)
        : base(runner, "git_status", "Shows the current branch, commits ahead/behind its upstream, and changed files.", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? gitArguments,
        [NotNullWhen(false)] out string? message)
    {
        gitArguments = ["status", "--porcelain=v1", "--branch"];
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(string standardOutput)
    {
        string? branch = null;
        var ahead = 0;
        var behind = 0;
        var entries = new List<object>();

        foreach (var line in standardOutput.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                (branch, ahead, behind) = ParseBranchLine(line["## ".Length..]);
            }
            else if (line.Length >= 4)
            {
                entries.Add(new { status = line[..2], path = Unquote(line[3..]) });
            }
        }

        return McpToolResult.Json(new { branch, ahead, behind, entries });
    }

    // Examples: "main...origin/main [ahead 1, behind 2]", "main", "No commits yet on main", "HEAD (no branch)".
    private static (string? Branch, int Ahead, int Behind) ParseBranchLine(string text)
    {
        const string noCommitsPrefix = "No commits yet on ";

        if (text.StartsWith(noCommitsPrefix, StringComparison.Ordinal))
        {
            return (text[noCommitsPrefix.Length..], 0, 0);
        }

        var ahead = 0;
        var behind = 0;
        var trackingStart = text.IndexOf(" [", StringComparison.Ordinal);

        if (trackingStart >= 0)
        {
            foreach (var part in text[(trackingStart + 2)..].TrimEnd(']').Split(", "))
            {
                if (part.StartsWith("ahead ", StringComparison.Ordinal) && int.TryParse(part["ahead ".Length..], out var a))
                {
                    ahead = a;
                }
                else if (part.StartsWith("behind ", StringComparison.Ordinal) && int.TryParse(part["behind ".Length..], out var b))
                {
                    behind = b;
                }
            }

            text = text[..trackingStart];
        }

        var upstreamSeparator = text.IndexOf("...", StringComparison.Ordinal);
        var local = upstreamSeparator >= 0 ? text[..upstreamSeparator] : text;

        return (local == "HEAD (no branch)" ? null : local, ahead, behind);
    }

    // git C-quotes paths that contain special characters, e.g. "dir/with \"quote\".txt".
    private static string Unquote(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            return path[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return path;
    }
}
