using System.Text.Json;
using DotnetMcpToolkit.Docker;

namespace DotnetMcpToolkit.Tests.Docker;

public sealed class DockerInspectToolTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-rf")]
    [InlineData("web;rm")]
    [InlineData("a b")]
    [InlineData("web\\n")]
    public async Task InvokeAsync_InvalidTarget_ReturnsErrorWithoutRunningDocker(string target)
    {
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args($$"""{"target": "{{target}}"}"""));

        Assert.True(result.IsError);
        Assert.Contains("target must be", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_MissingTarget_ReturnsError()
    {
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args("{}"));

        Assert.True(result.IsError);
    }

    [Theory]
    [InlineData("pod")]
    [InlineData("CONTAINER")]
    [InlineData("")]
    public async Task InvokeAsync_UnknownType_ReturnsErrorWithoutRunningDocker(string type)
    {
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: "no-such-docker-binary-" + Guid.NewGuid().ToString("N")));

        var result = await tool.InvokeAsync(Args($$"""{"target": "web", "type": "{{type}}"}"""));

        Assert.True(result.IsError);
        Assert.Equal("type must be one of: container, image, network, volume", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_NoType_InspectsContainerByDefault()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"; printf '[]'");
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(Args("""{"target": "web"}"""));

        Assert.Equal("inspect --type container web ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_ImageTypeAndDigestTarget_BuildsInspectArguments()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s ' \"$@\" > \"$(dirname \"$0\")/args.txt\"; printf '[]'");
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: fake.Path));

        await tool.InvokeAsync(Args("""{"target": "library/nginx:1.27@sha256", "type": "image"}"""));

        Assert.Equal("inspect --type image library/nginx:1.27@sha256 ", File.ReadAllText(fake.ArgumentsPath));
    }

    [Fact]
    public async Task InvokeAsync_JsonArray_ReturnsIndentedDocument()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf '%s' '[{\"Id\":\"abc\",\"State\":{\"Running\":true}}]'");
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"target": "web"}"""));

        Assert.False(result.IsError);
        Assert.Contains("\n  {\n    \"Id\": \"abc\"", result.Content[0].Text);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(result.Content[0].Text!).RootElement.ValueKind);
    }

    [Fact]
    public async Task InvokeAsync_NonZeroExit_ReturnsErrorWithStderr()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("echo 'Error: No such object: web' >&2; exit 1");
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"target": "web"}"""));

        Assert.True(result.IsError);
        Assert.Contains("No such object: web", result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_OutputNotJson_ReturnsError()
    {
        if (!FakeDockerExecutable.IsSupported)
        {
            return;
        }

        using var fake = new FakeDockerExecutable("printf 'not json'");
        var tool = new DockerInspectTool(new DockerCliRunner(dockerExecutable: fake.Path));

        var result = await tool.InvokeAsync(Args("""{"target": "web"}"""));

        Assert.True(result.IsError);
        Assert.Equal("docker inspect returned output that is not valid JSON", result.Content[0].Text);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;
}
