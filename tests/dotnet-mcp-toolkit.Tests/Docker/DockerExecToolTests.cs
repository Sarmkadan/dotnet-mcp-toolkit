using System.Text.Json;
using DotnetMcpToolkit.Docker;

namespace DotnetMcpToolkit.Tests.Docker;

public sealed class DockerExecToolTests
{
    private const string _missingDocker = "no-such-docker-binary";

    [Theory]
    [InlineData("")]
    [InlineData("-rf")]
    [InlineData("web;rm")]
    public async Task InvokeAsync_InvalidContainer_ReturnsErrorWithoutRunningDocker(string container)
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker));

        var result = await tool.InvokeAsync(Args($$"""{"container": "{{container}}", "command": ["ls"]}"""));

        Assert.True(result.IsError);
        Assert.Contains("container must be", result.Content[0].Text);
    }

    [Theory]
    [InlineData("""{"container": "web"}""")]
    [InlineData("""{"container": "web", "command": []}""")]
    [InlineData("""{"container": "web", "command": "ls"}""")]
    [InlineData("""{"container": "web", "command": ["ls", ""]}""")]
    [InlineData("""{"container": "web", "command": ["ls", "  "]}""")]
    [InlineData("""{"container": "web", "command": ["ls", 1]}""")]
    public async Task InvokeAsync_InvalidCommand_ReturnsErrorWithoutRunningDocker(string json)
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker));

        var result = await tool.InvokeAsync(Args(json));

        Assert.True(result.IsError);
        Assert.Equal("command must be a non-empty array of non-empty strings", result.Content[0].Text);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("")]
    [InlineData("relative/path")]
    public async Task InvokeAsync_WorkdirNotAbsolute_ReturnsError(string workdir)
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker));

        var result = await tool.InvokeAsync(Args($$"""{"container": "web", "command": ["ls"], "workdir": "{{workdir}}"}"""));

        Assert.True(result.IsError);
        Assert.Equal("workdir must be an absolute path starting with '/'", result.Content[0].Text);
    }

    [Theory]
    [InlineData("-privileged")]
    [InlineData("root user")]
    public async Task InvokeAsync_InvalidUser_ReturnsErrorWithoutRunningDocker(string user)
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker));

        var result = await tool.InvokeAsync(Args($$"""{"container": "web", "command": ["ls"], "user": "{{user}}"}"""));

        Assert.True(result.IsError);
        Assert.Contains("user must be", result.Content[0].Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task InvokeAsync_TimeoutOutOfRange_ReturnsError(int timeoutSeconds)
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker));

        var result = await tool.InvokeAsync(Args($$"""{"container": "web", "command": ["ls"], "timeoutSeconds": {{timeoutSeconds}}}"""));

        Assert.True(result.IsError);
        Assert.Equal("timeoutSeconds must be between 1 and 600", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_AllOptions_BuildsExecArguments()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(Args("""{"container": "web", "command": ["ls", "-la", "/app"], "workdir": "/app", "user": "1000:1000", "timeoutSeconds": 5}"""));

        Assert.Equal("exec -w /app -u 1000:1000 web ls -la /app ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_OptionsOmitted_BuildsMinimalExecArguments()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(Args("""{"container": "web", "command": ["echo", "hi"]}"""));

        Assert.Equal("exec web echo hi ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_Success_ReturnsExitCodeAndOutput()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf 'out\\n'; printf 'err\\n' >&2; exit 0");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web", "command": ["ls"]}"""));

        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal(0, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("out\n", json.RootElement.GetProperty("stdout").GetString());
        Assert.Equal("err\n", json.RootElement.GetProperty("stderr").GetString());
    }

    [Fact]
    public async Task InvokeAsync_CommandExitsNonZero_ReturnsSuccessfulResultWithExitCode()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("echo 'boom' >&2; exit 3");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web", "command": ["false"]}"""));

        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(result.Content[0].Text!);
        Assert.Equal(3, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("boom\n", json.RootElement.GetProperty("stderr").GetString());
    }

    [Theory]
    [InlineData(125)]
    [InlineData(126)]
    [InlineData(127)]
    public async Task InvokeAsync_DockerRuntimeExitCode_ReturnsErrorWithStderr(int exitCode)
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable($"echo 'OCI runtime exec failed' >&2; exit {exitCode}");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web", "command": ["nope"]}"""));

        Assert.True(result.IsError);
        Assert.Contains("OCI runtime exec failed", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_DockerExecutableMissing_ReturnsError()
    {
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: _missingDocker + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args("""{"container": "web", "command": ["ls"]}"""));

        Assert.True(result.IsError);
        Assert.Equal("docker executable not found", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_CommandExceedsTimeout_ReturnsErrorNamingTheTimeout()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("sleep 30");
        var tool = new DockerExecTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"container": "web", "command": ["sleep", "30"], "timeoutSeconds": 1}"""));

        Assert.True(result.IsError);
        Assert.Contains("timed out after 1 seconds", result.Content[0].Text);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;
}
