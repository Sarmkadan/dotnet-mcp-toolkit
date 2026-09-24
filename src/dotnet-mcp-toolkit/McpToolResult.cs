namespace DotnetMcpToolkit;

/// <summary>
/// Result of a tool invocation, mapped to the MCP <c>CallToolResult</c> shape:
/// a list of content blocks plus an <c>isError</c> flag.
/// </summary>
/// <param name="Content">Content blocks returned to the model.</param>
/// <param name="IsError">
/// <see langword="true"/> when the tool ran but reported a domain error (e.g. SQL syntax error).
/// Protocol-level failures are signalled via JSON-RPC errors instead.
/// </param>
public sealed record McpToolResult(IReadOnlyList<McpContent> Content, bool IsError = false)
{
    /// <summary>
    /// Creates a successful result with a single text block.
    /// </summary>
    /// <param name="text">The text payload.</param>
    public static McpToolResult Text(string text)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates an error result with a single text block describing the failure.
    /// </summary>
    /// <param name="message">Human-readable error message for the model.</param>
    public static McpToolResult Error(string message)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates a successful result whose single text block is <paramref name="value"/> serialized as JSON.
    /// </summary>
    /// <typeparam name="T">Type of the payload.</typeparam>
    /// <param name="value">Value to serialize.</param>
    public static McpToolResult Json<T>(T value)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// A content block in a tool result. Only <c>text</c> is modelled for now; image and resource blocks may be added later.
/// </summary>
/// <param name="Type">Content type discriminator, e.g. <c>"text"</c>.</param>
/// <param name="Text">Text payload for <c>text</c> blocks.</param>
public sealed record McpContent(string Type, string? Text);
