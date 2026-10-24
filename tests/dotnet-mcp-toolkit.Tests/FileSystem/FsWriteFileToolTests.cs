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
    public async Task InvokeAsync_NewFileInNewDirectory_CreatesParentsAndWrites()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "new/deep/a.txt", "content": "x" }"""));

        Assert.False(result.IsError);
        Assert.Equal("x", await File.ReadAllTextAsync(Path.Combine(_root, "new", "deep", "a.txt")));
    }

    [Fact]
    public async Task InvokeAsync_NewFile_ReturnsPathAndBytesWritten()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "a.txt", "content": "héllo" }"""));

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal("a.txt", doc.RootElement.GetProperty("path").GetString());
        Assert.Equal(Encoding.UTF8.GetByteCount("héllo"), doc.RootElement.GetProperty("bytesWritten").GetInt32());
    }

    [Fact]
    public async Task InvokeAsync_MissingParentAndCreateDirectoriesFalse_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "sub/a.txt", "content": "x", "createDirectories": false }"""));

        Assert.True(result.IsError);
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
    }

    [Fact]
    public async Task InvokeAsync_OverwriteFalseOnExistingFile_ReturnsErrorAndKeepsContent()
    {
        var file = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(file, "original");

        var result = await _tool.InvokeAsync(Args("""{ "path": "a.txt", "content": "new", "overwrite": false }"""));

        Assert.True(result.IsError);
        Assert.Equal("original", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task InvokeAsync_ExistingFileDefaultOverwrite_ReplacesContent()
    {
        var file = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(file, "original");

        var result = await _tool.InvokeAsync(Args("""{ "path": "a.txt", "content": "new" }"""));

        Assert.False(result.IsError);
        Assert.Equal("new", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task InvokeAsync_Utf8Content_WritesWithoutBom()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "a.txt", "content": "héllo" }"""));

        Assert.False(result.IsError);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "a.txt"));
        Assert.NotEqual([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), bytes);
    }

    [Fact]
    public async Task InvokeAsync_PathEscapesRoot_ReturnsErrorAndWritesNothing()
    {
        var escapeName = "escape-" + Guid.NewGuid().ToString("N") + ".txt";

        var result = await _tool.InvokeAsync(Args($$"""{ "path": "../{{escapeName}}", "content": "x" }"""));

        Assert.True(result.IsError);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, escapeName)));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task InvokeAsync_MissingContent_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "a.txt" }"""));

        Assert.True(result.IsError);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
