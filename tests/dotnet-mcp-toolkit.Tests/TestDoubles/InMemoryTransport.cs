namespace DotnetMcpToolkit.Tests.TestDoubles;

/// <summary>
/// Transport that replays queued incoming messages and records everything the server writes.
/// </summary>
internal sealed class InMemoryTransport : IMcpTransport
{
    private readonly Queue<string> _incoming;

    /// <summary>
    /// Creates a transport that will deliver <paramref name="incoming"/> in FIFO order.
    /// </summary>
    public InMemoryTransport(Queue<string> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        _incoming = incoming;
    }

    /// <summary>
    /// Messages written by the server, in order.
    /// </summary>
    public List<string> Written { get; } = [];

    /// <inheritdoc />
    public ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(_incoming.Count > 0 ? _incoming.Dequeue() : null);
    }

    /// <inheritdoc />
    public ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Written.Add(json);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
