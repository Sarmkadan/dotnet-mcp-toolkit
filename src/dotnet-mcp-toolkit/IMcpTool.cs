using System.Text.Json;

namespace DotnetMcpToolkit;

/// <summary>
/// A single MCP tool: a named operation with a JSON Schema describing its input,
/// invoked by the client through <c>tools/call</c>.
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// Gets the tool name, unique within its server (e.g. <c>sql_query</c>, <c>fs_read_file</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a human-readable description shown to the model so it knows when to use the tool.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON Schema (draft 2020-12) of the <c>arguments</c> object accepted by <see cref="InvokeAsync"/>.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Executes the tool with the given arguments.
    /// </summary>
    /// <param name="arguments">The <c>arguments</c> object from the <c>tools/call</c> request, or <see langword="null"/> when none were supplied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool result to be serialized into the response.</returns>
    Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default);
}
