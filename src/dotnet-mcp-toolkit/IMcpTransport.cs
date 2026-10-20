namespace DotnetMcpToolkit;

/// <summary>
/// A bidirectional stream of JSON-RPC 2.0 messages. Implementations: stdio (newline-delimited JSON,
/// used by Claude Desktop / Claude Code / Copilot), and Streamable HTTP.
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>
    /// Reads the next raw JSON-RPC message from the client.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message as a JSON string, or <see langword="null"/> when the client has closed the connection.</returns>
    ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a raw JSON-RPC message to the client.
    /// </summary>
    /// <param name="json">A complete JSON-RPC 2.0 response or notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default);
}

