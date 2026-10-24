using System.Text.Json;
using DotnetMcpToolkit.Tests.TestDoubles;

namespace DotnetMcpToolkit.Tests;

public class McpServerTests
{
    private const string _serverName = "test-server";
    private const string _serverVersion = "1.2.3";

    private static IMcpServer CreateServer(params IMcpTool[] tools)
    {
        var builder = new McpServerBuilder(_serverName, _serverVersion);

        foreach (var tool in tools)
        {
            builder.AddTool(tool);
        }

        return builder.Build();
    }

    private static async Task<InMemoryTransport> RunWithMessagesAsync(IMcpServer server, params string[] messages)
    {
        var transport = new InMemoryTransport(new Queue<string>(messages));

        await server.RunAsync(transport);

        return transport;
    }

    private static JsonDocument ParseWritten(InMemoryTransport transport, int index = 0)
    {
        return JsonDocument.Parse(transport.Written[index]);
    }

    [Fact]
    public async Task RunAsync_Initialize_ReturnsServerInfoAndProtocolVersion()
    {
        // Arrange
        var server = CreateServer();
        var request = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(1, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(_serverName, result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(_serverVersion, result.GetProperty("serverInfo").GetProperty("version").GetString());
        Assert.False(string.IsNullOrEmpty(result.GetProperty("protocolVersion").GetString()));
    }

    [Fact]
    public async Task RunAsync_Ping_ReturnsEmptyObjectResult()
    {
        // Arrange
        var server = CreateServer();
        var request = """{"jsonrpc":"2.0","id":2,"method":"ping"}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        Assert.Equal(2, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Object, response.RootElement.GetProperty("result").ValueKind);
        Assert.Empty(response.RootElement.GetProperty("result").EnumerateObject());
    }

    [Fact]
    public async Task RunAsync_ToolsList_ReturnsRegisteredToolsWithSchema()
    {
        // Arrange
        var server = CreateServer(
            new FakeTool("alpha", _ => McpToolResult.Empty, "First tool"),
            new FakeTool("beta", _ => McpToolResult.Empty, "Second tool"));
        var request = """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        var tools = response.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
        Assert.Equal("alpha", tools[0].GetProperty("name").GetString());
        Assert.Equal("First tool", tools[0].GetProperty("description").GetString());
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.Equal("beta", tools[1].GetProperty("name").GetString());
        Assert.Equal("Second tool", tools[1].GetProperty("description").GetString());
        Assert.Equal("object", tools[1].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ToolsCall_InvokesToolAndReturnsContent()
    {
        // Arrange
        var tool = new FakeTool("echo", args => McpToolResult.Text($"x={args!.Value.GetProperty("x").GetInt32()}"));
        var server = CreateServer(tool);
        var request = """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"echo","arguments":{"x":5}}}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        Assert.Equal(5, tool.LastArguments!.Value.GetProperty("x").GetInt32());
        using var response = ParseWritten(transport);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("x=5", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.False(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ToolsCallWithErrorResult_SetsIsErrorTrue()
    {
        // Arrange
        var tool = new FakeTool("fails", _ => McpToolResult.Error("bad input"));
        var server = CreateServer(tool);
        var request = """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"fails","arguments":{}}}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("bad input", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task RunAsync_ToolsCallUnknownTool_ReturnsInvalidParamsError()
    {
        // Arrange
        var server = CreateServer(new FakeTool("known", _ => McpToolResult.Empty));
        var request = """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"missing"}}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        Assert.Equal(-32602, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(response.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task RunAsync_ToolsCallToolThrows_ReturnsIsErrorNotProtocolError()
    {
        // Arrange
        var tool = new FakeTool("boom", _ => throw new InvalidOperationException("kaboom"));
        var server = CreateServer(tool);
        var request = """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"boom","arguments":{}}}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        Assert.False(response.RootElement.TryGetProperty("error", out _));
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("kaboom", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task RunAsync_UnknownMethod_ReturnsMethodNotFound()
    {
        // Arrange
        var server = CreateServer();
        var request = """{"jsonrpc":"2.0","id":8,"method":"resources/list"}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        Assert.Equal(8, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(-32601, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RunAsync_MalformedJson_ReturnsParseErrorWithNullId()
    {
        // Arrange
        var server = CreateServer();

        // Act
        var transport = await RunWithMessagesAsync(server, "{not json");

        // Assert
        using var response = ParseWritten(transport);
        Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(-32700, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RunAsync_Notification_ProducesNoResponse()
    {
        // Arrange
        var server = CreateServer();
        var request = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        Assert.Empty(transport.Written);
    }

    [Fact]
    public async Task RunAsync_StringId_IsEchoedAsString()
    {
        // Arrange
        var server = CreateServer();
        var request = """{"jsonrpc":"2.0","id":"abc","method":"ping"}""";

        // Act
        var transport = await RunWithMessagesAsync(server, request);

        // Assert
        using var response = ParseWritten(transport);
        Assert.Equal(JsonValueKind.String, response.RootElement.GetProperty("id").ValueKind);
        Assert.Equal("abc", response.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RunAsync_TransportCloses_Completes()
    {
        // Arrange
        var server = CreateServer();
        var transport = new InMemoryTransport(new Queue<string>());

        // Act
        var exception = await Record.ExceptionAsync(() => server.RunAsync(transport));

        // Assert
        Assert.Null(exception);
        Assert.Empty(transport.Written);
    }

    [Fact]
    public async Task RunAsync_CancelledToken_StopsLoop()
    {
        // Arrange
        var server = CreateServer();
        var transport = new BlockingTransport();
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(transport, cts.Token);
        await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await cts.CancelAsync();
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        // Cancellation is the documented way to stop the loop: RunAsync returns normally instead of throwing.
        Assert.Same(run, finished);
        await run;
        Assert.Empty(transport.Written);
    }

    /// <summary>
    /// Transport whose read never completes until the token passed to it is cancelled.
    /// </summary>
    private sealed class BlockingTransport : IMcpTransport
    {
        private readonly TaskCompletionSource<string?> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once the server has entered <see cref="ReadMessageAsync"/>.
        /// </summary>
        public Task ReadStarted => _readStarted.Task;

        /// <summary>
        /// Messages written by the server; always empty for this transport.
        /// </summary>
        public List<string> Written { get; } = [];

        public async ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return await _gate.Task.WaitAsync(cancellationToken);
        }

        public ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default)
        {
            Written.Add(json);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
