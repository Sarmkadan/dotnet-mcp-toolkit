using System.Text;

namespace DotnetMcpToolkit;

/// <summary>
/// <see cref="IMcpTransport"/> over the process's standard input and output.
/// One JSON-RPC message per line; nothing else may be written to stdout while the server runs.
/// </summary>
public sealed class StdioTransport : IMcpTransport
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a transport over the given streams.
    /// </summary>
    /// <param name="input">Stream to read client messages from (normally <c>Console.OpenStandardInput()</c>).</param>
    /// <param name="output">Stream to write responses to (normally <c>Console.OpenStandardOutput()</c>).</param>
    public StdioTransport(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        _reader = new StreamReader(
            input,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: true);

        _writer = new StreamWriter(
            output,
            new UTF8Encoding(false),
            leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\n",
        };
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                return null;
            }

            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            return trimmed;
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (json.Contains('\n') || json.Contains('\r'))
        {
            throw new ArgumentException("A JSON-RPC message must be a single line.", nameof(json));
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _reader.Dispose();

        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);

        _writeLock.Dispose();
    }
}
