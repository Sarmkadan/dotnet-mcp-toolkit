using System.Text.Json;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit.Tests.Git;

public sealed class GitLogToolTests : IDisposable
{
    private readonly GitTestRepository _repo = new();
    private readonly GitRepoFixture _fixture = new();

    public void Dispose()
    {
        _repo.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_TwoCommits_ReturnsNewestFirst()
    {
        _repo.WriteFile("a.txt", "one");
        _repo.CommitAll("first commit");
        _repo.WriteFile("a.txt", "two");
        _repo.CommitAll("second commit");
        var tool = new GitLogTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        var commits = ParseJson(result).EnumerateArray().ToList();
        Assert.False(result.IsError);
        Assert.Equal(2, commits.Count);
        Assert.Equal("second commit", commits[0].GetProperty("subject").GetString());
        Assert.Equal("Test Author", commits[0].GetProperty("author").GetString());
        Assert.Equal(40, commits[0].GetProperty("sha").GetString()!.Length);
    }

    [Fact]
    public async Task InvokeAsync_PathFilter_ReturnsOnlyCommitsTouchingPath()
    {
        _repo.WriteFile("a.txt", "one");
        _repo.CommitAll("touch a");
        _repo.WriteFile("b.txt", "one");
        _repo.CommitAll("touch b");
        var tool = new GitLogTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt" }));

        var subjects = ParseJson(result).EnumerateArray().Select(c => c.GetProperty("subject").GetString()).ToList();
        Assert.Equal(["touch a"], subjects);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task InvokeAsync_MaxCountOutOfRange_ReturnsError(int maxCount)
    {
        var tool = new GitLogTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { maxCount }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_RepositoryWithoutCommits_ReturnsGitError()
    {
        var tool = new GitLogTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_Default_ReturnsCommitsNewestFirst()
    {
        var tool = new GitLogTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(null);

        var commits = ParseJson(result).EnumerateArray().ToList();
        Assert.False(result.IsError);
        Assert.Equal(2, commits.Count);
        Assert.Equal("fix: shout", commits[0].GetProperty("subject").GetString());
        Assert.Matches("^[0-9a-f]{40}$", commits[0].GetProperty("sha").GetString()!);
        Assert.True(DateTimeOffset.TryParse(commits[0].GetProperty("date").GetString(), out _));
    }

    [Fact]
    public async Task InvokeAsync_MaxCountOne_ReturnsSingleCommit()
    {
        var tool = new GitLogTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { maxCount = 1 }));

        var commits = ParseJson(result).EnumerateArray().ToList();
        Assert.Single(commits);
        Assert.Equal("fix: shout", commits[0].GetProperty("subject").GetString());
    }

    [Fact]
    public async Task InvokeAsync_PathFilter_ReturnsOnlyTouchingCommits()
    {
        var tool = new GitLogTool(new GitCommandRunner(_fixture.Root));

        var forA = ParseJson(await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "a.txt" })));
        var forB = ParseJson(await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { path = "b.txt" })));

        Assert.Equal(2, forA.GetArrayLength());
        Assert.Equal(1, forB.GetArrayLength());
    }

    private static JsonElement ParseJson(DotnetMcpToolkit.McpToolResult result)
    {
        using var document = JsonDocument.Parse(result.Content[0].Text!);
        return document.RootElement.Clone();
    }
}
