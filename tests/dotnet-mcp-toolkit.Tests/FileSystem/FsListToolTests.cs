using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class FsListToolTests : IClassFixture<TempTreeFixture>
{
    private readonly TempTreeFixture _tree;
    private readonly FsListTool _tool;

    public FsListToolTests(TempTreeFixture tree)
    {
        _tree = tree;
        _tool = new FsListTool(new PathSandbox(tree.Root));
    }

    [Fact]
    public async Task InvokeAsync_NoArguments_ListsRootEntriesSorted()
    {
        var result = await _tool.InvokeAsync(null);

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var paths = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!)
            .ToArray();
        Assert.Equal(["docs", "readme.md", "src"], paths);
    }

    [Fact]
    public async Task InvokeAsync_Recursive_IncludesNestedFilesWithForwardSlashPaths()
    {
        var result = await _tool.InvokeAsync(Args("""{ "recursive": true }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var paths = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!)
            .ToArray();
        Assert.Contains("src/Program.cs", paths);
        Assert.Contains("src/bin/app.dll", paths);
        Assert.Contains("docs/notes.txt", paths);
        Assert.All(paths, p => Assert.DoesNotContain('\\', p));
    }

    [Fact]
    public async Task InvokeAsync_PathEscapesRoot_ReturnsErrorResultNotException()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "../.." }"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_FileEntry_HasSizeAndModified()
    {
        var result = await _tool.InvokeAsync(null);

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var entry = doc.RootElement.GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("path").GetString() == "readme.md");
        Assert.Equal("file", entry.GetProperty("type").GetString());
        Assert.Equal(new FileInfo(Path.Combine(_tree.Root, "readme.md")).Length, entry.GetProperty("size").GetInt64());
        Assert.True(DateTimeOffset.TryParse(
            entry.GetProperty("modified").GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _));
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
