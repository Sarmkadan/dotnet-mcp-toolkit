using System.Text.Json;
using DotnetMcpToolkit.Database;
using DotnetMcpToolkit.Tests.TestDoubles;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderTests : IDisposable
{
    private readonly List<DirectoryInfo> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        // ThrowIfNullOrWhiteSpace raises ArgumentNullException for null, a subclass of ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new McpServerBuilder(name!, "1.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceVersion_ThrowsArgumentException(string? version)
    {
        // ThrowIfNullOrWhiteSpace raises ArgumentNullException for null, a subclass of ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new McpServerBuilder("test", version!));
    }

    [Fact]
    public void Build_NoTools_ReturnsServerWithEmptyToolList()
    {
        var server = new McpServerBuilder("my-server", "2.3.4").Build();

        Assert.Equal("my-server", server.Name);
        Assert.Equal("2.3.4", server.Version);
        Assert.Empty(server.Tools);
    }

    [Fact]
    public void AddTool_ReturnsSameBuilderInstance()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        var result = builder.AddTool(new FakeTool("tool_a", _ => McpToolResult.Empty));

        Assert.Same(builder, result);
    }

    [Fact]
    public void AddTool_NullTool_ThrowsArgumentNullException()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentNullException>(() => builder.AddTool(null!));
    }

    [Fact]
    public void AddTool_ToolWithEmptyName_ThrowsArgumentException()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentException>(() => builder.AddTool(new NamelessTool()));
    }

    [Fact]
    public void Build_TwoToolsWithSameName_ThrowsInvalidOperationExceptionNamingTool()
    {
        var builder = new McpServerBuilder("test", "1.0.0")
            .AddTool(new FakeTool("duplicate_name", _ => McpToolResult.Empty))
            .AddTool(new FakeTool("duplicate_name", _ => McpToolResult.Empty));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("duplicate_name", ex.Message);
    }

    [Fact]
    public void Build_ToolNamesDifferingInCase_AreDistinct()
    {
        var builder = new McpServerBuilder("test", "1.0.0")
            .AddTool(new FakeTool("fs_read", _ => McpToolResult.Empty))
            .AddTool(new FakeTool("FS_READ", _ => McpToolResult.Empty));

        var server = builder.Build();

        Assert.Equal(["fs_read", "FS_READ"], server.Tools.Select(t => t.Name));
    }

    [Fact]
    public void Build_PreservesRegistrationOrder()
    {
        var builder = new McpServerBuilder("test", "1.0.0")
            .AddTool(new FakeTool("charlie", _ => McpToolResult.Empty))
            .AddTool(new FakeTool("alpha", _ => McpToolResult.Empty))
            .AddTool(new FakeTool("bravo", _ => McpToolResult.Empty));

        var server = builder.Build();

        Assert.Equal(["charlie", "alpha", "bravo"], server.Tools.Select(t => t.Name));
    }

    [Fact]
    public void Build_CalledTwice_ReturnsIndependentServersWithSameTools()
    {
        var builder = new McpServerBuilder("test", "1.0.0")
            .AddTool(new FakeTool("tool_a", _ => McpToolResult.Empty))
            .AddTool(new FakeTool("tool_b", _ => McpToolResult.Empty));

        var first = builder.Build();
        var second = builder.Build();

        Assert.NotSame(first, second);
        Assert.Equal(["tool_a", "tool_b"], first.Tools.Select(t => t.Name));
        Assert.Equal(["tool_a", "tool_b"], second.Tools.Select(t => t.Name));

        // Tools added after the first Build must not leak into the already-built server.
        builder.AddTool(new FakeTool("tool_c", _ => McpToolResult.Empty));

        Assert.Equal(2, first.Tools.Count);
        Assert.Equal(["tool_a", "tool_b"], first.Tools.Select(t => t.Name));
    }

    [Fact]
    public void AddFileSystem_ReadOnly_DoesNotRegisterWriteTool()
    {
        var root = CreateTempDirectory();

        var server = new McpServerBuilder("test", "1.0.0").AddFileSystem(root.FullName, readOnly: true).Build();

        var names = server.Tools.Select(t => t.Name).ToList();
        Assert.Contains("fs_list", names);
        Assert.Contains("fs_read_file", names);
        Assert.Contains("fs_search", names);
        Assert.DoesNotContain("fs_write_file", names);
    }

    [Fact]
    public void AddFileSystem_Writable_RegistersWriteTool()
    {
        var root = CreateTempDirectory();

        var server = new McpServerBuilder("test", "1.0.0").AddFileSystem(root.FullName, readOnly: false).Build();

        Assert.Contains("fs_write_file", server.Tools.Select(t => t.Name));
    }

    [Fact]
    public void AddDatabase_UnsupportedProviderWithoutPackage_ThrowsNotSupportedException()
    {
        // PostgreSql is not shipped in the core package (only Sqlite is); it must be supplied by
        // DotnetMcpToolkit.Database.PostgreSql, so the builder rejects it instead of silently registering nothing.
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<NotSupportedException>(() => builder.AddDatabase("Host=localhost", DatabaseProvider.PostgreSql));
    }

    private DirectoryInfo CreateTempDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("mcp-builder-");
        _tempDirectories.Add(directory);
        return directory;
    }

    /// <summary>
    /// Tool with an empty name; <see cref="FakeTool"/> refuses to construct one, so this stub exists only for the
    /// <see cref="McpServerBuilder.AddTool"/> validation test.
    /// </summary>
    private sealed class NamelessTool : IMcpTool
    {
        public string Name => string.Empty;

        public string Description => "Tool without a name";

        public JsonElement InputSchema => JsonDocument.Parse("{}").RootElement.Clone();

        public Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(McpToolResult.Empty);
        }
    }
}
