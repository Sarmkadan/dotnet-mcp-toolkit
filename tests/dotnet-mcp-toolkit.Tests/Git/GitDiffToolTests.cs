using System.Text.Json;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit.Tests.Git;

public sealed class GitDiffToolTests : IDisposable
{
    private readonly GitTestRepository _repo = new();
    private readonly GitRepoFixture _fixture = new();

    public void Dispose()
    {
        _repo.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_UnstagedChange_ReturnsUnifiedDiff()
    {
        _repo.WriteFile("a.txt", "one\n");
        _repo.CommitAll("initial");
        _repo.WriteFile("a.txt", "one\ntwo\n");
        var tool = new GitDiffTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);
        Assert.Contains("+two", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_StagedFlag_ShowsOnlyStagedChanges()
    {
        _repo.WriteFile("a.txt", "one\n");
        _repo.CommitAll("initial");
        _repo.WriteFile("a.txt", "one\ntwo\n");
        _repo.Run("add", "a.txt");
        var tool = new GitDiffTool(new GitCommandRunner(_repo.Root));

        var unstaged = await tool.InvokeAsync(null);
        var staged = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { staged = true }));

        Assert.Equal(string.Empty, unstaged.Content[0].Text);
        Assert.Contains("+two", staged.Content[0].Text);
    }

    [Theory]
    [InlineData("--output=evil")]
    [InlineData("HEAD;rm")]
    [InlineData("HEAD:a.txt")]
    public async Task InvokeAsync_InvalidRef_ReturnsErrorWithoutRunningGit(string revision)
    {
        var tool = new GitDiffTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { @ref = revision }));

        Assert.True(result.IsError);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task InvokeAsync_ContextLinesOutOfRange_ReturnsError(int contextLines)
    {
        var tool = new GitDiffTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { contextLines }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_RefHeadTilde1_ShowsLineChange()
    {
        var tool = new GitDiffTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { @ref = "HEAD~1" }));

        Assert.False(result.IsError);
        Assert.Contains("-two", result.Content[0].Text);
        Assert.Contains("+TWO", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_WorkingTree_ShowsUncommittedChange()
    {
        var tool = new GitDiffTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);
        Assert.Contains("b.txt", result.Content[0].Text);
        Assert.Contains("+buzz", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_RefStartingWithDash_ReturnsError()
    {
        var tool = new GitDiffTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { @ref = "--output=/tmp/x" }));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_UnknownRef_ReturnsError()
    {
        var tool = new GitDiffTool(new GitCommandRunner(_fixture.Root));

        var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { @ref = "no-such-ref" }));

        Assert.True(result.IsError);
    }
}
