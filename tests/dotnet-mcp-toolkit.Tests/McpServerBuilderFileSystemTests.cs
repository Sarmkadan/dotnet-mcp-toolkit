using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "builderfs-" + Guid.NewGuid().ToString("N"));

    public McpServerBuilderFileSystemTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AddFileSystem_ReadWrite_RegistersAllFourTools()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddFileSystem(_root, readOnly: false);

        Assert.Equal(["fs_list", "fs_read_file", "fs_search", "fs_write_file"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddFileSystem_ReadOnly_OmitsWriteTool()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddFileSystem(_root, readOnly: true);

        Assert.Equal(["fs_list", "fs_read_file", "fs_search"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddFileSystem_ReturnsSameBuilder()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Same(builder, builder.AddFileSystem(_root));
    }
}
