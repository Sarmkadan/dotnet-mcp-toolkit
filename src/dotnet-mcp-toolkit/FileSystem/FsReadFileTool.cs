using System.Text;
using System.Text.Json;

namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// MCP tool <c>fs_read_file</c>: reads a text file inside the sandbox,
/// with optional line offset and limit.
/// </summary>
public sealed class FsReadFileTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["path"],
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the file to read"
                },
                "offset": {
                    "type": "integer",
                    "description": "1-based line number to start reading from"
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of lines to return (max 5000)"
                }
            }
        }
        """).RootElement.Clone();

    private const long _maxFileSizeBytes = 10L * 1024 * 1024;
    private const int _maxLimit = 5000;
    private const int _binaryDetectionBytes = 8 * 1024;

    private readonly PathSandbox _sandbox;

    /// <summary>
    /// Creates the tool bound to the given <paramref name="sandbox"/>.
    /// </summary>
    /// <param name="sandbox">The sandbox that constrains accessible paths.</param>
    public FsReadFileTool(PathSandbox sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        _sandbox = sandbox;
    }

    /// <inheritdoc />
    public string Name => "fs_read_file";

    /// <inheritdoc />
    public string Description => "Reads a text file inside the sandbox with optional line offset and limit.";

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

        var offset = ToolArguments.GetInt32(arguments, "offset");
        var limit = ToolArguments.GetInt32(arguments, "limit");

        if (limit > _maxLimit)
        {
            limit = _maxLimit;
        }

        string resolved;
        try
        {
            resolved = _sandbox.Resolve(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            return McpToolResult.Error(ex.Message);
        }

        if (!File.Exists(resolved))
        {
            return McpToolResult.Error($"File not found: {path}");
        }

        var fileInfo = new FileInfo(resolved);
        var hasRangeArgs = offset.HasValue || limit.HasValue;

        if (fileInfo.Length > _maxFileSizeBytes && !hasRangeArgs)
        {
            return McpToolResult.Error(
                $"File is {fileInfo.Length / (1024 * 1024):F1} MB, exceeding the 10 MB limit. " +
                "Use offset and limit to read a portion.");
        }

        // Binary detection: check first 8 KB for NUL bytes.
        if (await IsBinaryAsync(resolved, cancellationToken).ConfigureAwait(false))
        {
            return McpToolResult.Error("binary file");
        }

        var lines = new List<string>();
        var lineNumber = 0;
        var startLine = offset ?? 1;
        var maxLines = limit ?? _maxLimit;

        await foreach (var line in File.ReadLinesAsync(resolved, Encoding.UTF8, cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;

            if (lineNumber < startLine)
            {
                continue;
            }

            if (lines.Count >= maxLines)
            {
                break;
            }

            lines.Add(line);
        }

        return McpToolResult.Text(string.Join('\n', lines));
    }

    private static async Task<bool> IsBinaryAsync(string filePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[_binaryDetectionBytes];
        int bytesRead;

        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, _binaryDetectionBytes, useAsync: true))
        {
            bytesRead = await stream.ReadAsync(buffer.AsMemory(0, _binaryDetectionBytes), cancellationToken).ConfigureAwait(false);
        }

        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
