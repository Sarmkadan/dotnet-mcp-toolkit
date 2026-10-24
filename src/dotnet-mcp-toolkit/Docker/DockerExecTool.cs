using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// MCP tool <c>docker_exec</c>: runs a command inside a running container. Registered only when
/// <see cref="DockerOptions.AllowExec"/> is set, because it executes code in the container.
/// </summary>
public sealed class DockerExecTool : DockerToolBase
{
    private const int _defaultTimeoutSeconds = 60;
    private const int _maxTimeoutSeconds = 600;

    private const int _exitCodeTimeout = 124;
    private const int _exitCodeCannotInvoke = 126;
    private const int _exitCodeNotFound = 127;
    private const int _exitCodeDockerError = 125;

    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "container": {
                    "type": "string",
                    "description": "Name or id of a running container"
                },
                "command": {
                    "type": "array",
                    "items": { "type": "string", "minLength": 1 },
                    "minItems": 1,
                    "description": "Program and its arguments, run without a shell, e.g. [\"ls\", \"-la\", \"/app\"]"
                },
                "workdir": {
                    "type": "string",
                    "description": "Absolute working directory inside the container"
                },
                "user": {
                    "type": "string",
                    "description": "User to run as, e.g. \"app\" or \"1000:1000\""
                },
                "timeoutSeconds": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 600,
                    "description": "Time limit in seconds (1-600, default 60)"
                }
            },
            "required": ["container", "command"]
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the daemon that hosts the container.</param>
    public DockerExecTool(DockerCliRunner runner)
        : base(runner, "docker_exec", "Runs a command inside a running container without a shell. Returns exitCode, stdout and stderr; a non-zero exitCode is a result, not an error.", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? dockerArguments,
        [NotNullWhen(false)] out string? message)
    {
        dockerArguments = null;

        var container = ToolArguments.GetString(arguments, "container");

        if (container is null || !DockerPatterns.ContainerName().IsMatch(container))
        {
            message = "container must be a container name or id of 1-128 characters: letters, digits, '_', '.', '-', starting with a letter or digit";

            return false;
        }

        var command = ReadCommand(arguments);

        if (command is null)
        {
            message = "command must be a non-empty array of non-empty strings";

            return false;
        }

        var workdir = ToolArguments.GetString(arguments, "workdir");

        if (workdir is not null && !workdir.StartsWith('/'))
        {
            message = "workdir must be an absolute path starting with '/'";

            return false;
        }

        var user = ToolArguments.GetString(arguments, "user");

        if (user is not null && !DockerPatterns.UserName().IsMatch(user))
        {
            message = "user must be a name, a uid or user:group of 1-128 characters: letters, digits, '_', '.', ':', '-'";

            return false;
        }

        var timeoutSeconds = ToolArguments.GetInt32(arguments, "timeoutSeconds", _defaultTimeoutSeconds) ?? _defaultTimeoutSeconds;

        if (timeoutSeconds is < 1 or > _maxTimeoutSeconds)
        {
            message = $"timeoutSeconds must be between 1 and {_maxTimeoutSeconds}";

            return false;
        }

        var args = new List<string> { "exec" };

        if (workdir is not null)
        {
            args.Add("-w");
            args.Add(workdir);
        }

        if (user is not null)
        {
            args.Add("-u");
            args.Add(user);
        }

        args.Add(container);
        args.AddRange(command);

        dockerArguments = args;
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override TimeSpan? GetTimeout(JsonElement? arguments)
    {
        var timeoutSeconds = ToolArguments.GetInt32(arguments, "timeoutSeconds", _defaultTimeoutSeconds) ?? _defaultTimeoutSeconds;

        return TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>
    /// A command that docker could not start (or that timed out) is a tool error. Any other exit code
    /// means the command ran, so it is reported as a normal result.
    /// </summary>
    /// <param name="result">The command outcome.</param>
    /// <returns><see langword="true"/> when docker itself failed or the time limit was hit.</returns>
    protected override bool IsFailure(DockerCommandResult result)
    {
        return result.ExitCode is _exitCodeDockerError or _exitCodeCannotInvoke or _exitCodeNotFound or _exitCodeTimeout;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(DockerCommandResult result)
    {
        return McpToolResult.Json(new
        {
            exitCode = result.ExitCode,
            stdout = result.StandardOutput,
            stderr = result.StandardError,
        });
    }

    /// <summary>
    /// Reads the <c>command</c> array. Returns <see langword="null"/> when it is missing, empty, or holds a non-string or blank entry.
    /// </summary>
    private static List<string>? ReadCommand(JsonElement? arguments)
    {
        if (arguments is not { } element
            || !element.TryGetProperty("command", out var property)
            || property.ValueKind != JsonValueKind.Array
            || property.GetArrayLength() == 0)
        {
            return null;
        }

        var command = new List<string>(property.GetArrayLength());

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return null;
            }

            command.Add(item.GetString()!);
        }

        return command;
    }
}
