using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// MCP tool <c>docker_inspect</c>: returns the low-level JSON description of a container, image, network or volume.
/// </summary>
public sealed class DockerInspectTool : DockerToolBase
{
    private const string _defaultType = "container";

    private static readonly string[] _allowedTypes = ["container", "image", "network", "volume"];

    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "target": {
                    "type": "string",
                    "description": "Name or id of the object to inspect"
                },
                "type": {
                    "type": "string",
                    "enum": ["container", "image", "network", "volume"],
                    "description": "Kind of object to inspect (default container)"
                }
            },
            "required": ["target"]
        }
        """).RootElement.Clone();

    /// <summary>
    /// Creates the tool bound to the given <paramref name="runner"/>.
    /// </summary>
    /// <param name="runner">Runner for the daemon to query.</param>
    public DockerInspectTool(DockerCliRunner runner)
        : base(runner, "docker_inspect", "Returns the detailed JSON description of a container, image, network or volume (docker inspect).", _inputSchema)
    {
    }

    /// <inheritdoc />
    protected override bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? dockerArguments,
        [NotNullWhen(false)] out string? message)
    {
        var target = ToolArguments.GetString(arguments, "target");

        if (target is null || !DockerPatterns.InspectTarget().IsMatch(target))
        {
            dockerArguments = null;
            message = "target must be a name or id of 1-256 characters: letters, digits, '_', '.', ':', '/', '@', '-', starting with a letter or digit";

            return false;
        }

        var type = ToolArguments.GetString(arguments, "type", _defaultType) ?? _defaultType;

        if (!_allowedTypes.Contains(type, StringComparer.Ordinal))
        {
            dockerArguments = null;
            message = "type must be one of: container, image, network, volume";

            return false;
        }

        dockerArguments = ["inspect", "--type", type, target];
        message = null;

        return true;
    }

    /// <inheritdoc />
    protected override McpToolResult CreateResult(DockerCommandResult result)
    {
        JsonDocument document;

        try
        {
            // docker prints a JSON array with one entry per inspected object.
            document = JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            return McpToolResult.Error("docker inspect returned output that is not valid JSON");
        }

        using (document)
        {
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                document.RootElement.WriteTo(writer);
            }

            return McpToolResult.Text(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length));
        }
    }
}
