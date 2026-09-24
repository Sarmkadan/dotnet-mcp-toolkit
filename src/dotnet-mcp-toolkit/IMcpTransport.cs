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

/// <summary>
/// <see cref="IMcpTransport"/> over the process's standard input and output.
/// One JSON-RPC message per line; nothing else may be written to stdout while the server runs.
/// </summary>
public sealed class StdioTransport : IMcpTransport
{
    /// <summary>
    /// Creates a transport over the given streams.
    /// </summary>
    /// <param name="input">Stream to read client messages from (normally <c>Console.OpenStandardInput()</c>).</param>
    /// <param name="output">Stream to write responses to (normally <c>Console.OpenStandardOutput()</c>).</param>
    public StdioTransport(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}
