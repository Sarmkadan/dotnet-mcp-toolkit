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

    private const int _maxLimit = 5000;
    private const int _binaryDetectionBytes = 8 * 1024;

    private readonly PathSandbox _sandbox;
    private readonly long _maxFileSizeBytes;

    /// <summary>
    /// Creates the tool bound to the given <paramref name="sandbox"/>.
    /// </summary>
    /// <param name="sandbox">The sandbox that constrains accessible paths.</param>
    /// <param name="maxFileSizeBytes">
    /// Largest file that may be read without an offset and limit, in bytes; at least 1. Defaults to 10 MB.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="sandbox"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFileSizeBytes"/> is less than 1.</exception>
    public FsReadFileTool(PathSandbox sandbox, long maxFileSizeBytes = 10L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileSizeBytes, 1);

        _sandbox = sandbox;
        _maxFileSizeBytes = maxFileSizeBytes;
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
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentError)
        {
            return argumentError;
        }

        if (ToolArgumentValidation.RequirePath(arguments, "path", out var path) is { } pathError)
        {
            return pathError;
        }

        if (ToolArgumentValidation.OptionalInt32(arguments, "offset", 1, int.MaxValue, 1, out var startLine) is { } offsetError)
        {
            return offsetError;
        }

        if (ToolArgumentValidation.OptionalInt32(arguments, "limit", 1, _maxLimit, _maxLimit, out var maxLines) is { } limitError)
        {
            return limitError;
        }

        var hasRangeArgs = ToolArgumentValidation.HasArgument(arguments, "offset") ||
            ToolArgumentValidation.HasArgument(arguments, "limit");

        try
        {
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

            if (fileInfo.Length > _maxFileSizeBytes && !hasRangeArgs)
            {
                return McpToolResult.Error(
                    $"File is {fileInfo.Length / (1024 * 1024):F1} MB, exceeding the {_maxFileSizeBytes / (1024.0 * 1024):0.##} MB limit. " +
                    "Use offset and limit to read a portion.");
            }

            // Binary detection: check first 8 KB for NUL bytes.
            if (await IsBinaryAsync(resolved, cancellationToken).ConfigureAwait(false))
            {
                return McpToolResult.Error("binary file");
            }

            var lines = new List<string>();
            var lineNumber = 0;

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
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // FileNotFoundException and DirectoryNotFoundException are IOExceptions, so a file removed mid-read lands here too.
            return McpToolResult.Error($"Cannot read '{path}': {ex.Message}");
        }
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
