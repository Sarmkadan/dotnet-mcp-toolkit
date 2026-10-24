using DotnetMcpToolkit.Docker;

namespace DotnetMcpToolkit.Tests.Docker;

public sealed class DockerCliRunnerTests
{
    [Fact]
    public async Task RunAsync_MissingExecutable_ReturnsExitCode127()
    {
        var runner = new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N"));

        var result = await runner.RunAsync(["ps"], CancellationToken.None);

        Assert.Equal(127, result.ExitCode);
        Assert.Equal("docker executable not found", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_DockerHostSet_PrependsHostFlag()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s\\n' \"$@\"");
        var runner = new DockerCliRunner("tcp://example:2375", fake.Path);

        var result = await runner.RunAsync(["ps", "-a"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("--host\ntcp://example:2375\nps\n-a", result.StandardOutput.TrimEnd());
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ReturnsStderrWithoutThrowing()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("echo 'Error: No such container' >&2; exit 1");
        var runner = new DockerCliRunner(dockerExecutable: fake.Path);

        var result = await runner.RunAsync(["logs", "missing"], CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No such container", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ProcessExceedsTimeout_KillsItAndReturnsExitCode124()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("sleep 30");
        var runner = new DockerCliRunner(dockerExecutable: fake.Path);

        var result = await runner.RunAsync(["ps"], CancellationToken.None, TimeSpan.FromMilliseconds(200));

        Assert.Equal(124, result.ExitCode);
        Assert.Contains("timed out", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsOperationCanceledException()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("sleep 30");
        var runner = new DockerCliRunner(dockerExecutable: fake.Path);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(["ps"], cts.Token));
    }

    [Fact]
    public async Task RunAsync_OutputExceedsCap_TruncatesWithMarker()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        // 5 MiB of 'a' on stdout, more than the 4 MiB per-stream capture limit.
        using var fake = new FakeDockerExecutable("head -c 5242880 /dev/zero | tr '\\0' 'a'");
        var runner = new DockerCliRunner(dockerExecutable: fake.Path);

        var result = await runner.RunAsync(["logs"], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.EndsWith("\n[truncated]", result.StandardOutput);
        Assert.True(result.StandardOutput.Length < 5 * 1024 * 1024);
    }

    [Fact]
    public void Constructor_EmptyExecutable_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new DockerCliRunner(dockerExecutable: " "));
    }
}
