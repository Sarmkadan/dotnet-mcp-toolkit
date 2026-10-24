using System.Text;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class FsWriteFileToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fswrite-" + Guid.NewGuid().ToString("N"));
    private readonly FsWriteFileTool _tool;

    public FsWriteFileToolTests()
    {
        Directory.CreateDirectory(_root);
        _tool = new FsWriteFileTool(new PathSandbox(_root));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_NewFile_WritesUtf8WithoutBom()
    {
        var args = Args(new { path = "a.txt", content = "héllo" });

        var result = await _tool.InvokeAsync(args);

        Assert.False(result.IsError);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "a.txt"));
        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), bytes);
    }

    [Fact]
    public async Task InvokeAsync_NewFile_ReturnsPathAndBytesWritten()
    {
        var args = Args(new { path = "a.txt", content = "héllo" });

        var result = await _tool.InvokeAsync(args);

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal("a.txt", doc.RootElement.GetProperty("path").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount("héllo"), doc.RootElement.GetProperty("bytesWritten").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_MissingParentDirectory_CreatesIt()
    {
        var args = Args(new { path = "sub/dir/a.txt", content = "x" });

        var result = await _tool.InvokeAsync(args);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_root, "sub", "dir", "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_MissingParentAndCreateDirectoriesFalse_ReturnsError()
    {
        var args = Args(new { path = "sub/a.txt", content = "x", createDirectories = false });

        var result = await _tool.InvokeAsync(args);

        Assert.True(result.IsError);
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
    }

    [Fact]
    public async Task InvokeAsync_ExistingFileAndOverwriteFalse_ReturnsErrorAndKeepsContent()
    {
        var file = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(file, "original");
        var args = Args(new { path = "a.txt", content = "new", overwrite = false });

        var result = await _tool.InvokeAsync(args);

        Assert.True(result.IsError);
        Assert.Equal("original", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task InvokeAsync_ExistingFileDefaultOverwrite_ReplacesContent()
    {
        var file = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(file, "original");
        var args = Args(new { path = "a.txt", content = "new" });

        var result = await _tool.InvokeAsync(args);

        Assert.False(result.IsError);
        Assert.Equal("new", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task InvokeAsync_PathEscapingSandbox_ReturnsErrorAndWritesNothing()
    {
        var args = Args(new { path = "../escape.txt", content = "x" });

        var result = await _tool.InvokeAsync(args);

        Assert.True(result.IsError);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escape.txt")));
    }

    [Fact]
    public async Task InvokeAsync_MissingContent_ReturnsError()
    {
        var args = Args(new { path = "a.txt" });

        var result = await _tool.InvokeAsync(args);

        Assert.True(result.IsError);
    }

    private static JsonElement Args(object value) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
}
