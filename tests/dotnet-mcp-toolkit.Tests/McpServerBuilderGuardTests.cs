using System.Text.Json;
using DotnetMcpToolkit.Database;
using DotnetMcpToolkit.Tests.TestDoubles;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderGuardTests
{
    [Fact]
    public void AddTool_NullTool_ThrowsArgumentNullException()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentNullException>(() => builder.AddTool(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTool_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.ThrowsAny<ArgumentException>(() => builder.AddTool(new NamedTool(name!)));
    }

    [Theory]
    [InlineData("fs read")]
    [InlineData("fs\tread")]
    [InlineData("fs_read\n")]
    public void AddTool_NameWithWhitespace_ThrowsArgumentExceptionNamingTool(string name)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        var ex = Assert.Throws<ArgumentException>(() => builder.AddTool(new NamedTool(name)));

        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void AddTool_NameLongerThan64Characters_ThrowsArgumentExceptionNamingTool()
    {
        var name = new string('a', 65);
        var builder = new McpServerBuilder("test", "1.0.0");

        var ex = Assert.Throws<ArgumentException>(() => builder.AddTool(new NamedTool(name)));

        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void AddTool_NameExactly64Characters_IsAccepted()
    {
        var name = new string('a', 64);
        var builder = new McpServerBuilder("test", "1.0.0");

        builder.AddTool(new FakeTool(name, _ => McpToolResult.Empty));

        Assert.Equal(name, builder.Tools.Single().Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFileSystem_NullOrWhitespaceRoot_ThrowsArgumentException(string? root)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.ThrowsAny<ArgumentException>(() => builder.AddFileSystem(root!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddGit_NullOrWhitespaceRepositoryPath_ThrowsArgumentException(string? repositoryPath)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.ThrowsAny<ArgumentException>(() => builder.AddGit(repositoryPath!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddDatabase_NullOrWhitespaceConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.ThrowsAny<ArgumentException>(() => builder.AddDatabase(connectionString!, DatabaseProvider.Sqlite));
    }

    [Fact]
    public void AddDatabase_UndefinedProvider_ThrowsArgumentOutOfRangeException()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddDatabase("Data Source=:memory:", (DatabaseProvider)99));
    }

    [Fact]
    public void AddDocker_HostWithoutScheme_ThrowsArgumentExceptionNamingHost()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        var ex = Assert.Throws<ArgumentException>(() => builder.AddDocker("localhost:2375"));

        Assert.Contains("localhost:2375", ex.Message);
        Assert.Empty(builder.Tools);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://localhost:2375")]
    public void AddDocker_EmptyOrUnsupportedScheme_ThrowsArgumentException(string dockerHost)
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentException>(() => builder.AddDocker(dockerHost));
    }

    [Fact]
    public void AddDocker_TcpHost_IsAccepted()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        builder.AddDocker("tcp://localhost:2375");

        Assert.Equal(["docker_inspect", "docker_logs", "docker_ps"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task RunAsync_NullTransport_ThrowsArgumentNullException()
    {
        var server = new McpServerBuilder("test", "1.0.0").Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() => server.RunAsync(null!));
    }

    [Fact]
    public async Task RunAsync_CalledWhileAlreadyRunning_ThrowsInvalidOperationException()
    {
        var server = new McpServerBuilder("test", "1.0.0").Build();
        var transport = new GatedTransport();
        using var cts = new CancellationTokenSource();
        var firstRun = server.RunAsync(transport, cts.Token);
        await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync(new InMemoryTransport([])));

        Assert.Equal("Server is already running", ex.Message);
        await cts.CancelAsync();
        await firstRun;
    }

    [Fact]
    public async Task RunAsync_CalledAfterPreviousRunCancelled_Succeeds()
    {
        var server = new McpServerBuilder("test", "1.0.0").Build();
        var transport = new GatedTransport();
        using var cts = new CancellationTokenSource();
        var firstRun = server.RunAsync(transport, cts.Token);
        await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await firstRun;

        var exception = await Record.ExceptionAsync(() => server.RunAsync(new InMemoryTransport([])));

        Assert.Null(exception);
    }

    /// <summary>
    /// Tool that reports a caller-supplied name without validating it, so the builder's name checks can be exercised.
    /// </summary>
    private sealed class NamedTool(string name) : IMcpTool
    {
        public string Name { get; } = name;

        public string Description => "Tool with a caller-supplied name";

        public JsonElement InputSchema => JsonDocument.Parse("{}").RootElement.Clone();

        public Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(McpToolResult.Empty);
        }
    }

    /// <summary>
    /// Transport whose read blocks until the token is cancelled.
    /// </summary>
    private sealed class GatedTransport : IMcpTransport
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once the server has entered <see cref="ReadMessageAsync"/>.
        /// </summary>
        public Task ReadStarted => _readStarted.Task;

        public async ValueTask<string?> ReadMessageAsync(CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }

        public ValueTask WriteMessageAsync(string json, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
