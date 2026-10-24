using System.Text;

namespace DotnetMcpToolkit.Tests;

public class StdioTransportTests
{
    private static readonly UTF8Encoding _utf8 = new(false);

    private static MemoryStream InputOf(string text) => new(_utf8.GetBytes(text));

    [Fact]
    public async Task ReadMessageAsync_TwoLines_ReturnsEachMessageInOrder()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf("{\"a\":1}\n{\"b\":2}\n"), new MemoryStream());

        // Act
        var first = await transport.ReadMessageAsync();
        var second = await transport.ReadMessageAsync();

        // Assert
        Assert.Equal("{\"a\":1}", first);
        Assert.Equal("{\"b\":2}", second);
    }

    [Fact]
    public async Task ReadMessageAsync_BlankLinesBetweenMessages_SkipsThem()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf("{\"a\":1}\n\n   \n{\"b\":2}\n"), new MemoryStream());

        // Act
        var first = await transport.ReadMessageAsync();
        var second = await transport.ReadMessageAsync();

        // Assert
        Assert.Equal("{\"a\":1}", first);
        Assert.Equal("{\"b\":2}", second);
    }

    [Fact]
    public async Task ReadMessageAsync_AtEndOfStream_ReturnsNull()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf("{\"a\":1}\n"), new MemoryStream());
        await transport.ReadMessageAsync();

        // Act
        var result = await transport.ReadMessageAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadMessageAsync_CrLfLineEndings_ReturnsTrimmedMessage()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf("{\"a\":1}\r\n"), new MemoryStream());

        // Act
        var result = await transport.ReadMessageAsync();

        // Assert
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public async Task ReadMessageAsync_Utf8Payload_PreservesCharacters()
    {
        // Arrange
        const string message = "{\"text\":\"Гамбург ✓\"}";
        await using var transport = new StdioTransport(InputOf(message + "\n"), new MemoryStream());

        // Act
        var result = await transport.ReadMessageAsync();

        // Assert
        Assert.Equal(message, result);
    }

    [Fact]
    public async Task WriteMessageAsync_SingleMessage_WritesLineTerminatedByLf()
    {
        // Arrange
        var output = new MemoryStream();
        await using var transport = new StdioTransport(InputOf(string.Empty), output);

        // Act
        await transport.WriteMessageAsync("{\"a\":1}");

        // Assert
        var bytes = output.ToArray();
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal((byte)0x0A, bytes[^1]);
        Assert.DoesNotContain((byte)0x0D, bytes);
        Assert.Equal(1, bytes.Count(b => b == 0x0A));
    }

    [Fact]
    public async Task WriteMessageAsync_MessageContainingNewline_ThrowsArgumentException()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());

        // Act
        var act = () => transport.WriteMessageAsync("{\"a\":\n1}").AsTask();

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteMessageAsync_NullOrWhitespaceMessage_ThrowsArgumentException(string json)
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());

        // Act
        var act = () => transport.WriteMessageAsync(json).AsTask();

        // Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task WriteMessageAsync_MessageContainingCarriageReturn_ThrowsArgumentException()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());

        // Act
        var act = () => transport.WriteMessageAsync("{\"a\":\r1}").AsTask();

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task WriteMessageAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());
        await transport.DisposeAsync();

        // Act
        var act = () => transport.WriteMessageAsync("{\"a\":1}").AsTask();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(act);
    }

    [Fact]
    public async Task WriteMessageAsync_ConcurrentWrites_DoNotInterleave()
    {
        // Arrange
        var output = new MemoryStream();
        await using var transport = new StdioTransport(InputOf(string.Empty), output);
        var payload = new string('x', 2048);
        var messages = Enumerable.Range(0, 50)
            .Select(i => $"{{\"id\":{i},\"data\":\"{payload}\"}}")
            .ToList();

        // Act
        await Task.WhenAll(messages.Select(m => transport.WriteMessageAsync(m).AsTask()));

        // Assert
        var lines = _utf8.GetString(output.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(messages.Count, lines.Length);
        Assert.All(lines, line => Assert.Contains(line, messages));
    }

    [Fact]
    public async Task WriteMessageAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => transport.WriteMessageAsync("{\"a\":1}", cts.Token).AsTask();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var transport = new StdioTransport(InputOf(string.Empty), new MemoryStream());
        await transport.DisposeAsync();

        // Act
        var second = await Record.ExceptionAsync(() => transport.DisposeAsync().AsTask());

        // Assert
        Assert.Null(second);
    }

    [Fact]
    public async Task ReadMessageAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new StdioTransport(InputOf("{\"a\":1}\n"), new MemoryStream());
        await transport.DisposeAsync();

        // Act
        var act = () => transport.ReadMessageAsync().AsTask();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(act);
    }

    [Fact]
    public async Task Constructor_LeavesUnderlyingStreamsOpen()
    {
        // Arrange
        var input = InputOf("{\"a\":1}\n");
        var output = new MemoryStream();
        var transport = new StdioTransport(input, output);

        // Act
        await transport.DisposeAsync();

        // Assert
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }
}
