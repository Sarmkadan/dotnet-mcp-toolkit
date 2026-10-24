using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class FsSearchToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fssearch-" + Guid.NewGuid().ToString("N"));
    private readonly FsSearchTool _tool;

    public FsSearchToolTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Foo.cs"), "class Foo {}\nint Needle = 1;\n");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "needle in text\n");
        File.WriteAllBytes(Path.Combine(_root, "image.bin"), [0x00, 0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65]);
        _tool = new FsSearchTool(new PathSandbox(_root));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task InvokeAsync_MatchingPattern_ReturnsPathLineAndText()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "Needle", glob = "*.cs" }));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var match = Assert.Single(doc.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal("src/Foo.cs", match.GetProperty("path").GetString());
        Assert.Equal(2, match.GetProperty("line").GetInt32());
        Assert.Equal("int Needle = 1;", match.GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvokeAsync_IgnoreCaseFalse_DoesNotMatchDifferentCase()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "needle", glob = "*.cs" }));

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Empty(doc.RootElement.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task InvokeAsync_IgnoreCaseTrue_MatchesDifferentCase()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "needle", glob = "*.cs", ignoreCase = true }));

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Single(doc.RootElement.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task InvokeAsync_BinaryFile_IsSkipped()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "needle", ignoreCase = true }));

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var paths = doc.RootElement.GetProperty("matches").EnumerateArray()
            .Select(m => m.GetProperty("path").GetString());
        Assert.DoesNotContain("image.bin", paths);
    }

    [Fact]
    public async Task InvokeAsync_MaxResultsReached_ReturnsTruncated()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "Needle|needle", ignoreCase = true, maxResults = 1 }));

        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Single(doc.RootElement.GetProperty("matches").EnumerateArray());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_InvalidRegex_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "([" }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_PathEscapingSandbox_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args(new { pattern = "x", path = ".." }));

        Assert.True(result.IsError);
    }

    private static JsonElement Args(object value) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
}
