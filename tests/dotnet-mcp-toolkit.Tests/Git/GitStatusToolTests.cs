using System.Text.Json;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit.Tests.Git;

public sealed class GitStatusToolTests : IDisposable
{
    private readonly GitTestRepository _repo = new();

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_CleanRepository_ReportsBranchWithNoEntries()
    {
        _repo.WriteFile("a.txt", "one");
        _repo.CommitAll("initial");
        var tool = new GitStatusTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        var root = ParseJson(result);
        Assert.False(result.IsError);
        Assert.Equal("main", root.GetProperty("branch").GetString());
        Assert.Empty(root.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task InvokeAsync_ModifiedAndUntrackedFiles_ListsBothEntries()
    {
        _repo.WriteFile("a.txt", "one");
        _repo.CommitAll("initial");
        _repo.WriteFile("a.txt", "changed");
        _repo.WriteFile("new.txt", "fresh");
        var tool = new GitStatusTool(new GitCommandRunner(_repo.Root));

        var result = await tool.InvokeAsync(null);

        var entries = ParseJson(result).GetProperty("entries").EnumerateArray()
            .Select(e => (Status: e.GetProperty("status").GetString(), Path: e.GetProperty("path").GetString()))
            .ToList();
        Assert.Contains((" M", "a.txt"), entries);
        Assert.Contains(("??", "new.txt"), entries);
    }

    [Fact]
    public async Task InvokeAsync_BranchAheadOfUpstream_ReportsAheadCount()
    {
        _repo.WriteFile("a.txt", "one");
        _repo.CommitAll("initial");
        _repo.Run("init", "-q", "--bare", _repo.BareRemote);
        _repo.Run("remote", "add", "origin", _repo.BareRemote);
        _repo.Run("push", "-q", "-u", "origin", "main");
        _repo.WriteFile("a.txt", "two");
        _repo.CommitAll("second");
        var tool = new GitStatusTool(new GitCommandRunner(_repo.Root));

        var root = ParseJson(await tool.InvokeAsync(null));

        Assert.Equal(1, root.GetProperty("ahead").GetInt32());
        Assert.Equal(0, root.GetProperty("behind").GetInt32());
    }

    private static JsonElement ParseJson(DotnetMcpToolkit.McpToolResult result)
    {
        using var document = JsonDocument.Parse(result.Content[0].Text!);
        return document.RootElement.Clone();
    }
}
