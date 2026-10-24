using System.Text.Json;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit.Tests.Git;

public sealed class GitBlameToolTests : IDisposable
{
    private readonly GitTestRepository _repo = new();
    private readonly GitRepoFixture _fixture = new();

    public void Dispose()
    {
        _repo.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_LinesFromTwoCommits_AttributesEachLine()
    {
        _repo.WriteFile("a.txt", "first line\n");
        _repo.CommitAll("first");
        _repo.WriteFile("a.txt", "first line\nsecond line\n");
        _repo.CommitAll("second");
        var tool = new GitBlameTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt" }));

        var lines = ParseJson(result).EnumerateArray().ToList();
        Assert.False(result.IsError);
        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].GetProperty("line").GetInt32());
        Assert.Equal("first", lines[0].GetProperty("summary").GetString());
        Assert.Equal("Test Author", lines[0].GetProperty("author").GetString());
        Assert.Equal("first line", lines[0].GetProperty("text").GetString());
        Assert.Equal("second", lines[1].GetProperty("summary").GetString());
        Assert.Equal("second line", lines[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvokeAsync_LineRange_ReturnsOnlyRequestedLines()
    {
        _repo.WriteFile("a.txt", "one\ntwo\nthree\n");
        _repo.CommitAll("initial");
        var tool = new GitBlameTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt", startLine = 2, endLine = 2 }));

        var lines = ParseJson(result).EnumerateArray().ToList();
        Assert.Single(lines);
        Assert.Equal(2, lines[0].GetProperty("line").GetInt32());
        Assert.Equal("two", lines[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvokeAsync_MissingPath_ReturnsError()
    {
        var tool = new GitBlameTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_StartLineAfterEndLine_ReturnsError()
    {
        var tool = new GitBlameTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt", startLine = 5, endLine = 2 }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_FileWithTwoAuthorsCommits_ReturnsPerLineSha()
    {
        var head = _fixture.Git("rev-parse", "HEAD").Trim();
        var parent = _fixture.Git("rev-parse", "HEAD~1").Trim();
        var tool = new GitBlameTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt" }));

        var lines = ParseJson(result).EnumerateArray().ToList();
        Assert.False(result.IsError);
        Assert.Equal(parent, lines[0].GetProperty("sha").GetString());
        Assert.Equal(head, lines[1].GetProperty("sha").GetString());
    }

    [Fact]
    public async Task InvokeAsync_FixtureLineRange_ReturnsOnlyRequestedLines()
    {
        var tool = new GitBlameTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt", startLine = 2, endLine = 3 }));

        var lines = ParseJson(result).EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines[0].GetProperty("line").GetInt32());
        Assert.Equal("TWO", lines[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task InvokeAsync_FixtureMissingPath_ReturnsError()
    {
        var tool = new GitBlameTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "missing.txt" }));

        Assert.True(result.IsError);
    }

    private static JsonElement ParseJson(DotnetMcpToolkit.McpToolResult result)
    {
        using var document = JsonDocument.Parse(result.Content[0].Text!);
        return document.RootElement.Clone();
    }
}
