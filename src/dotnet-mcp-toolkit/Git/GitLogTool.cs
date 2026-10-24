using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// MCP tool <c>git_log</c>: lists recent commits, optionally filtered by path, date and author.
/// </summary>
public sealed class GitLogTool : GitToolBase
{
    private const int _defaultMaxCount = 20;

    // Unit separator (U+001F) cannot appear in commit metadata, so it is a safe field delimiter.
    private const char _fieldSeparator = '\u001f';

    private readonly int _maxCount;
    private readonly int _defaultCount;

    private static JsonElement CreateInputSchema(int maxCount) => JsonDocument.Parse($$"""
        {
            "type": "object",
            "properties": {
                "maxCount": {
                    "type": "integer",
                    "description": "Maximum number of commits to return (1-{{maxCount}}, default {{Math.Min(_defaultMaxCount, maxCount)}})"
                },
                "path": {
                    "type": "string",
                    "description": "Only list commits that touch this path"
                },
                "since": {
                    "type": "string",
                    "description": "Only list commits newer than this date, e.g. \"2 weeks ago\" or \"2026-01-01\""
                },
                "author": {
                    "type": "string",
                    "description": "Only list commits whose author matches this pattern"
                }
            }
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the repository to inspect.</param>
    /// <param name="maxCount">
    /// Largest value a caller may request for <c>maxCount</c>; at least 1. The default is 20, or this value when it is smaller. Defaults to 500.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="runner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is less than 1.</exception>
    public GitLogTool(GitCommandRunner runner, int maxCount = 500)
        : base(runner, "git_log", "Lists recent commits (sha, author, date, subject), optionally filtered by path, date or author.", CreateInputSchema(maxCount))
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        _maxCount = maxCount;
        _defaultCount = Math.Min(_defaultMaxCount, maxCount);
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? gitArguments,
        [NotNullWhen(false)] out string? message)
    {
        var maxCount = ToolArguments.GetInt32(arguments, "maxCount", _defaultCount) ?? _defaultCount;

        if (maxCount is < 1 || maxCount > _maxCount)
        {
            gitArguments = null;
            message = $"maxCount must be between 1 and {_maxCount}";

            return false;
        }

        var args = new List<string>
        {
            "log",
            "--no-color",
            "--date=iso-strict",
            "--format=%H%x1f%an%x1f%ad%x1f%s",
            "-n",
            maxCount.ToString(CultureInfo.InvariantCulture),
        };

        if (ToolArguments.GetString(arguments, "since") is { Length: > 0 } since)
        {
            args.Add($"--since={since}");
        }

        if (ToolArguments.GetString(arguments, "author") is { Length: > 0 } author)
        {
            args.Add($"--author={author}");
        }

        if (ToolArguments.GetString(arguments, "path") is { Length: > 0 } path)
        {
            args.Add("--");
            args.Add(path);
        }

        gitArguments = args;
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(string standardOutput)
    {
        var commits = new List<object>();

        foreach (var line in standardOutput.Split('\n'))
        {
            var fields = line.Split(_fieldSeparator, 4);

            if (fields.Length == 4)
            {
                commits.Add(new { sha = fields[0], author = fields[1], date = fields[2], subject = fields[3] });
            }
        }

        return McpToolResult.Json(commits);
    }
}
