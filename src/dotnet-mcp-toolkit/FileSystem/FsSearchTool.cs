using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// MCP tool <c>fs_search</c>: searches text files inside the sandbox for lines matching a regular expression.
/// </summary>
public sealed class FsSearchTool : IMcpTool
{
    private static JsonElement CreateInputSchema(int maxResults) => JsonDocument.Parse($$"""
        {
            "type": "object",
            "required": ["pattern"],
            "properties": {
                "pattern": {
                    "type": "string",
                    "description": "Regular expression matched against each line"
                },
                "path": {
                    "type": "string",
                    "description": "Relative directory to search (default \".\")"
                },
                "glob": {
                    "type": "string",
                    "description": "File name filter, e.g. \"*.cs\" (default \"*\")"
                },
                "maxResults": {
                    "type": "integer",
                    "description": "Maximum number of matching lines to return (default {{Math.Min(_defaultMaxResults, maxResults)}}, max {{maxResults}})"
                },
                "ignoreCase": {
                    "type": "boolean",
                    "description": "Whether the pattern matches case-insensitively (default false)"
                }
            }
        }
        """).RootElement.Clone();

    private const long _maxFileSizeBytes = 5L * 1024 * 1024;
    private const int _binaryDetectionBytes = 8 * 1024;
    private const int _defaultMaxResults = 200;
    private const int _maxPatternLength = 1000;
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(1);

    private readonly PathSandbox _sandbox;
    private readonly int _maxResults;

    /// <summary>
    /// Creates the tool bound to the given <paramref name="sandbox"/>.
    /// </summary>
    /// <param name="sandbox">The sandbox that constrains accessible paths.</param>
    /// <param name="maxResults">
    /// Largest value a caller may request for <c>maxResults</c>; at least 1. The default is 200, or this value when it is smaller. Defaults to 1000.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="sandbox"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResults"/> is less than 1.</exception>
    public FsSearchTool(PathSandbox sandbox, int maxResults = 1000)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);

        _sandbox = sandbox;
        _maxResults = maxResults;
        InputSchema = CreateInputSchema(maxResults);
    }

    /// <inheritdoc />
    public string Name => "fs_search";

    /// <inheritdoc />
    public string Description => "Searches text files inside the sandbox for lines matching a regular expression. Skips binary files and files larger than 5 MB.";

    /// <inheritdoc />
    public JsonElement InputSchema { get; }

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        if (ToolArgumentValidation.RequireObjectArguments(arguments) is { } argumentError)
        {
            return argumentError;
        }

        if (ToolArgumentValidation.RequireString(arguments, "pattern", out var pattern) is { } patternError)
        {
            return patternError;
        }

        if (ToolArgumentValidation.RequireMaxLength(pattern, "pattern", _maxPatternLength) is { } patternLengthError)
        {
            return patternLengthError;
        }

        if (ToolArgumentValidation.OptionalPath(arguments, "path", ".", out var path) is { } pathError)
        {
            return pathError;
        }

        if (ToolArgumentValidation.OptionalString(arguments, "glob", "*", out var glob) is { } globError)
        {
            return globError;
        }

        if (!IsValidGlob(glob))
        {
            return McpToolResult.Error(
                "Argument 'glob' must be a file name pattern without path separators, '..' or NUL characters");
        }

        if (ToolArgumentValidation.OptionalInt32(arguments, "maxResults", 1, _maxResults, Math.Min(_defaultMaxResults, _maxResults), out var maxResults) is { } maxResultsError)
        {
            return maxResultsError;
        }

        if (ToolArgumentValidation.OptionalBoolean(arguments, "ignoreCase", false, out var ignoreCase) is { } ignoreCaseError)
        {
            return ignoreCaseError;
        }

        Regex regex;
        try
        {
            var options = RegexOptions.CultureInvariant;

            if (ignoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            regex = new Regex(pattern, options, _regexTimeout);
        }
        catch (ArgumentException ex)
        {
            return McpToolResult.Error($"Invalid regular expression: {ex.Message}");
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
                return McpToolResult.Error(ex.Message);
            }

            if (!Directory.Exists(resolved))
            {
                return McpToolResult.Error($"Directory not found: {path}");
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // Do not follow symbolic links or other reparse points, so the walk cannot leave the sandbox.
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            var matches = new List<object>();
            var truncated = false;

            foreach (var file in Directory.EnumerateFiles(resolved, glob, enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryGetSearchableFileSize(file, out var size) || size > _maxFileSizeBytes)
                {
                    continue;
                }

                if (await IsBinaryAsync(file, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var relativePath = _sandbox.ToRelative(file);
                var lineNumber = 0;

                await foreach (var line in File.ReadLinesAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false))
                {
                    lineNumber++;

                    if (!regex.IsMatch(line))
                    {
                        continue;
                    }

                    if (matches.Count >= maxResults)
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(new { path = relativePath, line = lineNumber, text = line });
                }

                if (truncated)
                {
                    break;
                }
            }

            return McpToolResult.Json(new { matches, truncated });
        }
        catch (RegexMatchTimeoutException)
        {
            return McpToolResult.Error("Regular expression timed out; simplify the pattern.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // DirectoryNotFoundException is an IOException, so a directory removed mid-walk lands here too.
            return McpToolResult.Error($"Cannot search '{path}': {ex.Message}");
        }
    }

    private static bool IsValidGlob(string glob) =>
        glob.IndexOfAny(['/', '\\', '\0']) < 0 && !glob.Contains("..", StringComparison.Ordinal);

    private static bool TryGetSearchableFileSize(string file, out long size)
    {
        try
        {
            size = new FileInfo(file).Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            size = 0;
            return false;
        }
    }

    private static async Task<bool> IsBinaryAsync(string filePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[_binaryDetectionBytes];
        int bytesRead;

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, _binaryDetectionBytes, useAsync: true);
            bytesRead = await stream.ReadAsync(buffer.AsMemory(0, _binaryDetectionBytes), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable files are treated as binary so that they are skipped.
            return true;
        }

        return buffer.AsSpan(0, bytesRead).Contains((byte)0);
    }
}
