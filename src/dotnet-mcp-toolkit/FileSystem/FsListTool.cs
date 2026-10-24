using System.Text.Json;

namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// MCP tool <c>fs_list</c>: lists files and directories inside the sandbox.
/// </summary>
public sealed class FsListTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to list (default \".\")"
                },
                "recursive": {
                    "type": "boolean",
                    "description": "Whether to list recursively (default false)"
                }
            }
        }
        """).RootElement.Clone();

    private readonly PathSandbox _sandbox;
    private readonly int _maxEntries;

    /// <summary>
    /// Creates the tool bound to the given <paramref name="sandbox"/>.
    /// </summary>
    /// <param name="sandbox">The sandbox that constrains accessible paths.</param>
    /// <param name="maxEntries">Largest number of entries returned in one call; at least 1. Defaults to 2000.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sandbox"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxEntries"/> is less than 1.</exception>
    public FsListTool(PathSandbox sandbox, int maxEntries = 2000)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);

        _sandbox = sandbox;
        _maxEntries = maxEntries;
    }

    /// <inheritdoc />
    public string Name => "fs_list";

    /// <inheritdoc />
    public string Description => "Lists files and directories inside the sandbox.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentError)
        {
            return Task.FromResult(argumentError);
        }

        if (ToolArgumentValidation.OptionalPath(arguments, "path", ".", out var path) is { } pathError)
        {
            return Task.FromResult(pathError);
        }

        if (ToolArgumentValidation.OptionalBoolean(arguments, "recursive", false, out var recursive) is { } recursiveError)
        {
            return Task.FromResult(recursiveError);
        }

        try
        {
            string resolved;
            try
            {
                resolved = _sandbox.Resolve(path);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Task.FromResult(McpToolResult.Error(ex.Message));
            }

            if (!Directory.Exists(resolved))
            {
                return Task.FromResult(McpToolResult.Error($"Directory not found: {path}"));
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = new List<object>();
            var truncated = false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(resolved, "*", option))
            {
                if (entries.Count >= _maxEntries)
                {
                    truncated = true;
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();

                string relativePath;
                try
                {
                    relativePath = _sandbox.ToRelative(entry);
                }
                catch
                {
                    continue;
                }

                try
                {
                    if (File.Exists(entry))
                    {
                        var info = new FileInfo(entry);
                        entries.Add(new
                        {
                            path = relativePath,
                            type = "file",
                            size = info.Length,
                            modified = info.LastWriteTimeUtc.ToString("o"),
                        });
                    }
                    else if (Directory.Exists(entry))
                    {
                        var info = new DirectoryInfo(entry);
                        entries.Add(new
                        {
                            path = relativePath,
                            type = "dir",
                            modified = info.LastWriteTimeUtc.ToString("o"),
                        });
                    }
                }
                catch
                {
                    // Skip entries whose metadata cannot be read.
                    continue;
                }
            }

            entries.Sort((a, b) => string.Compare(
                GetPath(a), GetPath(b), StringComparison.Ordinal));

            object result = truncated
                ? new { entries, truncated = true }
                : (object)new { entries };

            return Task.FromResult(McpToolResult.Json(result));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // DirectoryNotFoundException is an IOException, so a directory removed mid-walk lands here too.
            return Task.FromResult(McpToolResult.Error($"Cannot list '{path}': {ex.Message}"));
        }
    }

    private static string GetPath(object entry)
    {
        // Anonymous types have a "path" property accessible via reflection or JsonSerializer.
        // Use a simple approach: serialize and read.
        var json = JsonSerializer.Serialize(entry);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("path").GetString() ?? string.Empty;
    }
}
