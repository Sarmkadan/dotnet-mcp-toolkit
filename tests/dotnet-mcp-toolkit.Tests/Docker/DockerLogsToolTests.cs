using System.Text.Json;
using DotnetMcpToolkit.Docker;

namespace DotnetMcpToolkit.Tests.Docker;

public sealed class DockerLogsToolTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-rf")]
    [InlineData("web;rm")]
    [InlineData("a b")]
    public async Task InvokeAsync_InvalidContainerName_ReturnsErrorWithoutRunningDocker(string container)
    {
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args($$"""{"container": "{{container}}"}"""));

        Assert.True(result.IsError);
        Assert.Contains("container must be", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_MissingContainer_ReturnsError()
    {
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args("{}"));

        Assert.True(result.IsError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task InvokeAsync_TailOutOfRange_ReturnsError(int tail)
    {
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args($$"""{"container": "web", "tail": {{tail}}}"""));

        Assert.True(result.IsError);
        Assert.Equal("tail must be between 1 and 5000", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_AllOptions_BuildsLogsArguments()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"");
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(Args("""{"container": "web", "tail": 5, "since": "10m", "timestamps": true}"""));

        Assert.Equal("logs --tail 5 --since=10m -t web ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_Success_ReturnsStdoutFollowedByStderr()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf 'out\\n'; printf 'err\\n' >&2");
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web"}"""));

        Assert.False(result.IsError);
        Assert.Equal("out\nerr\n", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_NonZeroExit_ReturnsErrorWithStderr()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("echo 'Error: No such container: web' >&2; exit 1");
        var tool = new DockerLogsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web"}"""));

        Assert.True(result.IsError);
        Assert.Contains("No such container: web", result.Content[0].Text);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;
}
