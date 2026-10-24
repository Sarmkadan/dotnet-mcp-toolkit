using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// MCP tool <c>docker_logs</c>: returns the last lines of a container's logs.
/// </summary>
public sealed partial class DockerLogsTool : DockerToolBase
{
    private const int _defaultTail = 200;
    private const int _maxTail = 5000;

    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "container": {
                    "type": "string",
                    "description": "Container name or id"
                },
                "tail": {
                    "type": "integer",
                    "description": "Number of lines from the end of the logs (1-5000, default 200)"
                },
                "since": {
                    "type": "string",
                    "description": "Only logs newer than this, e.g. \"10m\", \"2026-01-01T00:00:00\" or a unix timestamp"
                },
                "timestamps": {
                    "type": "boolean",
                    "description": "Prefix each line with its timestamp (default false)"
                }
            },
            "required": ["container"]
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the daemon to query.</param>
    public DockerLogsTool(DockerCliRunner runner)
        : base(runner, "docker_logs", "Returns the last lines of a container's logs (stdout and stderr), optionally since a time and with timestamps.", _inputSchema)
    {
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex ContainerNamePattern();

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? dockerArguments,
        [NotNullWhen(false)] out string? message)
    {
        var container = ToolArguments.GetString(arguments, "container");

        if (container is null || !ContainerNamePattern().IsMatch(container))
        {
            dockerArguments = null;
            message = "container must be a container name or id of 1-128 characters: letters, digits, '_', '.', '-', starting with a letter or digit";

            return false;
        }

        var tail = ToolArguments.GetInt32(arguments, "tail", _defaultTail) ?? _defaultTail;

        if (tail is < 1 or > _maxTail)
        {
            dockerArguments = null;
            message = $"tail must be between 1 and {_maxTail}";

            return false;
        }

        var args = new List<string>
        {
            "logs",
            "--tail",
            tail.ToString(CultureInfo.InvariantCulture),
        };

        if (ToolArguments.GetString(arguments, "since") is { Length: > 0 } since)
        {
            args.Add($"--since={since}");
        }

        if (ToolArguments.GetBoolean(arguments, "timestamps"))
        {
            args.Add("-t");
        }

        args.Add(container);

        dockerArguments = args;
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(DockerCommandResult result)
    {
        // Docker writes the container's stderr stream to its own stderr, so both streams are returned together.
        var separator = result.StandardOutput.Length > 0
            && result.StandardError.Length > 0
            && !result.StandardOutput.EndsWith('\n')
            ? "\n"
            : string.Empty;

        return McpToolResult.Text(result.StandardOutput + separator + result.StandardError);
    }
}
