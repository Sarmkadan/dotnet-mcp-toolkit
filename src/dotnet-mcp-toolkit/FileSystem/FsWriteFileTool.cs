using System.Text;
using System.Text.Json;

namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// MCP tool <c>fs_write_file</c>: writes a UTF-8 text file inside the sandbox.
/// </summary>
public sealed class FsWriteFileTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["path", "content"],
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the file to write"
                },
                "content": {
                    "type": "string",
                    "description": "Full text content to write, encoded as UTF-8 without BOM"
                },
                "createDirectories": {
                    "type": "boolean",
                    "description": "Whether to create missing parent directories (default true)"
                },
                "overwrite": {
                    "type": "boolean",
                    "description": "Whether to replace an existing file (default true)"
                }
            }
        }
        """).RootElement.Clone();

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly PathSandbox _sandbox;

    /// <summary>
    /// Creates the tool bound to the given <paramref name="sandbox"/>.
    /// </summary>
    /// <param name="sandbox">The sandbox that constrains accessible paths.</param>
    public FsWriteFileTool(PathSandbox sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        _sandbox = sandbox;
    }

    /// <inheritdoc />
    public string Name => "fs_write_file";

    /// <inheritdoc />
    public string Description => "Writes a UTF-8 text file inside the sandbox, optionally creating parent directories and overwriting existing files.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetString(arguments, "path");

        if (string.IsNullOrWhiteSpace(path))
        {
            return McpToolResult.Error("Missing required argument: path");
        }

        var content = ToolArguments.GetString(arguments, "content");

        if (content is null)
        {
            return McpToolResult.Error("Missing required argument: content");
        }

        var createDirectories = ToolArguments.GetBoolean(arguments, "createDirectories", defaultValue: true);
        var overwrite = ToolArguments.GetBoolean(arguments, "overwrite", defaultValue: true);

        string resolved;
        try
        {
            resolved = _sandbox.Resolve(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            return McpToolResult.Error(ex.Message);
        }

        if (Directory.Exists(resolved))
        {
            return McpToolResult.Error($"Path is a directory: {path}");
        }

        if (File.Exists(resolved) && !overwrite)
        {
            return McpToolResult.Error($"File already exists and overwrite is false: {path}");
        }

        var parent = Path.GetDirectoryName(resolved);

        if (parent is not null && !Directory.Exists(parent))
        {
            if (!createDirectories)
            {
                return McpToolResult.Error($"Parent directory does not exist: {path}");
            }

            Directory.CreateDirectory(parent);
        }

        cancellationToken.ThrowIfCancellationRequested();

        await File.WriteAllTextAsync(resolved, content, _utf8NoBom, cancellationToken).ConfigureAwait(false);

        return McpToolResult.Json(new
        {
            path = _sandbox.ToRelative(resolved),
            bytesWritten = _utf8NoBom.GetByteCount(content),
        });
    }
}
