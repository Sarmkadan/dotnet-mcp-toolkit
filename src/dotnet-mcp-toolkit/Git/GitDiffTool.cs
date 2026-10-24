using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// MCP tool <c>git_diff</c>: shows unstaged, staged, or ref-relative changes as a unified diff.
/// </summary>
public sealed class GitDiffTool : GitToolBase
{
    private const int _defaultContextLines = 3;
    private const int _maxContextLines = 20;

    // Revision names only: letters, digits and the punctuation used by ref syntax (HEAD~1, main^2, @{-1}, v1.0/x).
    private static readonly Regex _refPattern = new(@"^[A-Za-z0-9_./~^@{}-]+\z", RegexOptions.CultureInvariant);

    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "ref": {
                    "type": "string",
                    "description": "Revision to diff against, e.g. HEAD~1 or a branch name"
                },
                "staged": {
                    "type": "boolean",
                    "description": "Show staged changes (index vs. HEAD) instead of unstaged changes"
                },
                "path": {
                    "type": "string",
                    "description": "Limit the diff to this path"
                },
                "contextLines": {
                    "type": "integer",
                    "description": "Lines of context around each change (0-20, default 3)"
                }
            }
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the repository to inspect.</param>
    public GitDiffTool(GitCommandRunner runner)
        : base(runner, "git_diff", "Shows a unified diff of unstaged or staged changes, or against a revision, optionally limited to a path.", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? gitArguments,
        [NotNullWhen(false)] out string? message)
    {
        var contextLines = ToolArguments.GetInt32(arguments, "contextLines", _defaultContextLines) ?? _defaultContextLines;

        if (contextLines is < 0 or > _maxContextLines)
        {
            gitArguments = null;
            message = $"contextLines must be between 0 and {_maxContextLines}";

            return false;
        }

        var args = new List<string>
        {
            "diff",
            "--no-color",
            // Stops a diff.external setting in the repository from running an arbitrary program.
            "--no-ext-diff",
            $"-U{contextLines.ToString(CultureInfo.InvariantCulture)}",
        };

        if (ToolArguments.GetBoolean(arguments, "staged"))
        {
            args.Add("--cached");
        }

        if (ToolArguments.GetString(arguments, "ref") is { Length: > 0 } revision)
        {
            // A leading '-' would be read as an option, so it is rejected together with any other unexpected character.
            if (revision.StartsWith('-') || !_refPattern.IsMatch(revision))
            {
                gitArguments = null;
                message = $"Invalid ref: {revision}";

                return false;
            }

            args.Add(revision);
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
}
