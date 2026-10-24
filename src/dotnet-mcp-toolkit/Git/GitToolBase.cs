using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// Shared plumbing for the git tools: validates the arguments, runs git through a <see cref="GitCommandRunner"/>
/// and maps the outcome to a tool result. A failed command becomes an error result carrying git's stderr;
/// a successful one is passed to <see cref="CreateResult"/>.
/// </summary>
public abstract class GitToolBase : IMcpTool
{
    private readonly GitCommandRunner _runner;

    /// <summary>
    /// Creates the shared part of a git tool.
    /// </summary>
    /// <param name="runner">Runner bound to the repository the tool operates on.</param>
    /// <param name="name">Tool name exposed to the client.</param>
    /// <param name="description">Description shown to the model.</param>
    /// <param name="inputSchema">JSON Schema of the arguments object.</param>
    protected GitToolBase(GitCommandRunner runner, string name, string description, JsonElement inputSchema)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        Name = name;
        Description = description;
        InputSchema = inputSchema;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public JsonElement InputSchema { get; }

    /// <inheritdoc />
    public async Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        if (!TryBuildArguments(arguments, out var gitArguments, out var message))
        {
            return McpToolResult.Error(message);
        }

        var result = await _runner.RunAsync(gitArguments, cancellationToken);

        if (!result.Succeeded)
        {
            return McpToolResult.Error(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git exited with code {result.ExitCode}"
                    : result.StandardError);
        }

        return CreateResult(result.StandardOutput);
    }

    /// <summary>
    /// Validates the tool arguments and builds the git argument list. Values are added as separate
    /// entries of the list, never concatenated into a shell string.
    /// </summary>
    /// <param name="arguments">The <c>arguments</c> object from the request, or <see langword="null"/>.</param>
    /// <param name="gitArguments">The arguments passed to <c>git</c> when the call is valid.</param>
    /// <param name="message">A message for the model when the arguments are invalid.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    protected abstract bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? gitArguments,
        [NotNullWhen(false)] out string? message);

    /// <summary>
    /// Converts the standard output of a successful git command into the tool result.
    /// </summary>
    /// <param name="standardOutput">Captured standard output of git.</param>
    /// <returns>The tool result. Defaults to the raw output as text.</returns>
    protected virtual McpToolResult CreateResult(string standardOutput)
    {
        return McpToolResult.Text(standardOutput);
    }
}
