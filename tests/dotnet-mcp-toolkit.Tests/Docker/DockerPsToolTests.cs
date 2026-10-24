using System.Text.Json;
using DotnetMcpToolkit.Docker;

namespace DotnetMcpToolkit.Tests.Docker;

public sealed class DockerPsToolTests
{
    private const string _psLine = """{"ID":"abc123","Names":"web","Image":"nginx:1.27","Status":"Up 2 hours","State":"running","Ports":"127.0.0.1:8080->80/tcp"}""";

    [Fact]
    public async Task InvokeAsync_DefaultArguments_RunsPsWithoutAllFlag()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"");
        var tool = new DockerPsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);
        Assert.Equal("ps --format {{json .}} ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_AllTrue_PassesAllFlag()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"");
        var tool = new DockerPsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(JsonDocument.Parse("""{"all": true}""").RootElement);

        Assert.Equal("ps --format {{json .}} -a ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_JsonLines_ReturnsContainerList()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s\\n' '" + _psLine + "'");
        var tool = new DockerPsTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(null);

        Assert.False(result.IsError);

        using var json = JsonDocument.Parse(result.Content[0].Text!);
        var container = json.RootElement[0];

        Assert.Equal("abc123", container.GetProperty("id").GetString());
        Assert.Equal("web", container.GetProperty("names").GetString());
        Assert.Equal("nginx:1.27", container.GetProperty("image").GetString());
        Assert.Equal("running", container.GetProperty("state").GetString());
        Assert.Equal("127.0.0.1:8080->80/tcp", container.GetProperty("ports").GetString());
    }

    [Fact]
    public async Task InvokeAsync_MissingDocker_ReturnsError()
    {
        var tool = new DockerPsTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(null);

        Assert.True(result.IsError);
        Assert.Equal("docker executable not found", result.Content[0].Text);
    }
}
