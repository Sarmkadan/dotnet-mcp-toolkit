using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// MCP tool <c>docker_ps</c>: lists containers, running ones only unless <c>all</c> is set.
/// </summary>
public sealed class DockerPsTool : DockerToolBase
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "all": {
                    "type": "boolean",
                    "description": "Include stopped containers (default false)"
                }
            }
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the daemon to query.</param>
    public DockerPsTool(DockerCliRunner runner)
        : base(runner, "docker_ps", "Lists containers (id, names, image, status, state, ports). Running containers only unless all is true.", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? dockerArguments,
        [NotNullWhen(false)] out string? message)
    {
        var args = new List<string> { "ps", "--format", "{{json .}}" };

        if (ToolArguments.GetBoolean(arguments, "all"))
        {
            args.Add("-a");
        }

        dockerArguments = args;
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(DockerCommandResult result)
    {
        var containers = new List<object>();

        try
        {
            // docker prints one JSON object per line for "--format {{json .}}".
            foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                containers.Add(ParseContainer(line));
            }
        }
        catch (JsonException)
        {
            return McpToolResult.Error("docker ps returned output that is not valid JSON");
        }

        return McpToolResult.Json(containers);
    }

    private static object ParseContainer(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        return new
        {
            id = ReadString(root, "ID"),
            names = ReadString(root, "Names"),
            image = ReadString(root, "Image"),
            status = ReadString(root, "Status"),
            state = ReadString(root, "State"),
            ports = ReadString(root, "Ports"),
        };
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
