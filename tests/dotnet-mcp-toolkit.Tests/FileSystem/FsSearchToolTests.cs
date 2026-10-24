using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class FsSearchToolTests : IClassFixture<TempTreeFixture>
{
    private readonly FsSearchTool _tool;

    public FsSearchToolTests(TempTreeFixture tree)
    {
        _tool = new FsSearchTool(new PathSandbox(tree.Root));
    }

    [Fact]
    public async Task InvokeAsync_RegexMatch_ReturnsPathLineAndText()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": "WriteLine" }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var match = Assert.Single(doc.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal("src/Program.cs", match.GetProperty("path").GetString());
        Assert.Equal(4, match.GetProperty("line").GetInt32());
        Assert.Equal(TempTreeFixture.HelloLine, match.GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvokeAsync_GlobFilter_RestrictsFiles()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": ".", "glob": "*.md" }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        var paths = doc.RootElement.GetProperty("matches").EnumerateArray()
            .Select(m => m.GetProperty("path").GetString()!)
            .ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.Equal("readme.md", p));
    }

    [Fact]
    public async Task InvokeAsync_IgnoreCase_MatchesDifferentCase()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": "writeline", "ignoreCase": true }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Single(doc.RootElement.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task InvokeAsync_IgnoreCaseDefault_DoesNotMatchDifferentCase()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": "writeline" }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Empty(doc.RootElement.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task InvokeAsync_InvalidRegex_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": "(" }"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_MaxResults_TruncatesAndFlags()
    {
        var result = await _tool.InvokeAsync(Args("""{ "pattern": ".", "maxResults": 5 }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal(5, doc.RootElement.GetProperty("matches").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_BinaryFiles_AreSkipped()
    {
        // app.dll contains the text "Console.WriteLine", so it would match if the binary check were missing.
        var result = await _tool.InvokeAsync(Args("""{ "pattern": "WriteLine", "glob": "*.dll" }"""));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Empty(doc.RootElement.GetProperty("matches").EnumerateArray());
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
