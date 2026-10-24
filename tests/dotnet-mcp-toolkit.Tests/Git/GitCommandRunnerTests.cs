using System.Diagnostics;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit.Tests.Git;

public sealed class GitCommandRunnerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "gitcommandrunner-" + Guid.NewGuid().ToString("N"));
    private readonly string _repo;
    private readonly GitRepoFixture _fixture = new();

    public GitCommandRunnerTests()
    {
        _repo = Path.Combine(_base, "repo");
        Directory.CreateDirectory(_repo);
        Git(_repo, "init", "-q");
    }

    public void Dispose()
    {
        Directory.Delete(_base, recursive: true);
        _fixture.Dispose();
    }

    [Fact]
    public void Constructor_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new GitCommandRunner(Path.Combine(_base, "missing")));
    }

    [Fact]
    public async Task RunAsync_ValidCommand_ReturnsStandardOutput()
    {
        var runner = new GitCommandRunner(_repo);

        var result = await runner.RunAsync(["rev-parse", "--is-inside-work-tree"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("true", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_FailingCommand_ReturnsNonZeroExitCodeWithoutThrowing()
    {
        var runner = new GitCommandRunner(_repo);

        var result = await runner.RunAsync(["no-such-subcommand"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no-such-subcommand", result.StandardError);
    }

    [Theory]
    [InlineData("--upload-pack=evil")]
    [InlineData("--exec=evil")]
    [InlineData("-c core.pager=evil")]
    public async Task RunAsync_ForbiddenArgument_ThrowsArgumentException(string argument)
    {
        var runner = new GitCommandRunner(_repo);

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(["ls-remote", argument], CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_SplitConfigOverride_ThrowsArgumentException()
    {
        var runner = new GitCommandRunner(_repo);

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(["-c", "core.pager=evil", "status"], CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_NullEntry_ThrowsArgumentException()
    {
        var runner = new GitCommandRunner(_repo);

        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(["status", null!], CancellationToken.None));
    }

    [Fact]
    public async Task IsRepositoryAsync_InsideWorkTree_ReturnsTrue()
    {
        var runner = new GitCommandRunner(_repo);

        Assert.True(await runner.IsRepositoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IsRepositoryAsync_BareRepository_ReturnsFalse()
    {
        // A bare repository is not a work tree, so this stays false even when the temp directory sits inside another repository.
        var bare = Path.Combine(_base, "bare");
        Directory.CreateDirectory(bare);
        Git(bare, "init", "--bare", "-q");
        var runner = new GitCommandRunner(bare);

        Assert.False(await runner.IsRepositoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RevParse_ReturnsZeroExitAndTrue()
    {
        var runner = new GitCommandRunner(_fixture.Root);

        var result = await runner.RunAsync(["rev-parse", "--is-inside-work-tree"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_InvalidCommand_ReturnsNonZeroWithStderr()
    {
        var runner = new GitCommandRunner(_fixture.Root);

        var result = await runner.RunAsync(["no-such-command"], CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no-such-command", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ArgumentStartingWithUploadPack_ThrowsArgumentException()
    {
        var runner = new GitCommandRunner(_fixture.Root);

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(["ls-remote", "--upload-pack=/bin/sh"], CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var runner = new GitCommandRunner(_fixture.Root);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(["status"], cts.Token));
    }

    [Fact]
    public void Constructor_NonExistentPath_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new GitCommandRunner(Path.Combine(_base, "nonexistent")));
    }

    private static void Git(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
