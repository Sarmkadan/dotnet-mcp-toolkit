using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// Shared plumbing for the docker tools: validates the arguments, runs the docker CLI through a
/// <see cref="DockerCliRunner"/> and maps the outcome to a tool result. A failed command becomes an error
/// result carrying docker's stderr; a successful one is passed to <see cref="CreateResult"/>.
/// </summary>
public abstract class DockerToolBase : IMcpTool
{
    private readonly DockerCliRunner _runner;

    /// <summary>
    /// Creates the shared part of a docker tool.
    /// </summary>
    /// <param name="runner">Runner bound to the daemon the tool operates on.</param>
    /// <param name="name">Tool name exposed to the client.</param>
    /// <param name="description">Description shown to the model.</param>
    /// <param name="inputSchema">JSON Schema of the arguments object.</param>
    protected DockerToolBase(DockerCliRunner runner, string name, string description, JsonElement inputSchema)
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
        if (!TryBuildArguments(arguments, out var dockerArguments, out var message))
        {
            return McpToolResult.Error(message);
        }

        var result = await _runner.RunAsync(dockerArguments, cancellationToken);

        if (!result.Succeeded)
        {
            return McpToolResult.Error(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"docker exited with code {result.ExitCode}"
                    : result.StandardError);
        }

        return CreateResult(result);
    }

    /// <summary>
    /// Validates the tool arguments and builds the docker argument list. Values are added as separate
    /// entries of the list, never concatenated into a shell string.
    /// </summary>
    /// <param name="arguments">The <c>arguments</c> object from the request, or <see langword="null"/>.</param>
    /// <param name="dockerArguments">The arguments passed to <c>docker</c> when the call is valid.</param>
    /// <param name="message">A message for the model when the arguments are invalid.</param>
    /// <returns><see langword="true"/> when the arguments are valid.</returns>
    protected abstract bool TryBuildArguments(
        JsonElement? arguments,
        [NotNullWhen(true)] out IReadOnlyList<string>? dockerArguments,
        [NotNullWhen(false)] out string? message);

    /// <summary>
    /// Converts a successful docker command into the tool result.
    /// </summary>
    /// <param name="result">The successful command outcome.</param>
    /// <returns>The tool result. Defaults to the raw standard output as text.</returns>
    protected virtual McpToolResult CreateResult(DockerCommandResult result)
    {
        return McpToolResult.Text(result.StandardOutput);
    }
}
