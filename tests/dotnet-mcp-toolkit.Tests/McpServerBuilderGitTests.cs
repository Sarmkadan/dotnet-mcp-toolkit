using DotnetMcpToolkit.Tests.Git;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderGitTests : IDisposable
{
    private readonly GitTestRepository _repo = new();

    public void Dispose()
    {
        _repo.Dispose();
    }

    [Fact]
    public void AddGit_ValidRepository_RegistersFourGitTools()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddGit(_repo.Root);

        Assert.Equal(["git_blame", "git_diff", "git_log", "git_status"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddGit_ReturnsSameBuilder()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Same(builder, builder.AddGit(_repo.Root));
    }

    [Fact]
    public void AddGit_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var missing = Path.Combine(_repo.Root, "missing");

        Assert.Throws<DirectoryNotFoundException>(() => new McpServerBuilder("test", "1.0.0").AddGit(missing));
    }
}
