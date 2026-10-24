using DotnetMcpToolkit.Options;

namespace DotnetMcpToolkit.Tests;

public sealed class McpServerBuilderDockerTests
{
    private static readonly string[] _readOnlyTools = ["docker_inspect", "docker_logs", "docker_ps"];

    [Fact]
    public void AddDocker_NoArguments_RegistersReadOnlyTools()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDocker();

        Assert.Equal(_readOnlyTools, builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddDocker_DockerHostOnly_RegistersReadOnlyTools()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDocker("unix:///var/run/docker.sock");

        Assert.Equal(_readOnlyTools, builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddDocker_DefaultOptions_DoesNotRegisterExec()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDocker(new DockerOptions());

        Assert.Equal(_readOnlyTools, builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddDocker_AllowExec_RegistersExecToo()
    {
        var builder = new McpServerBuilder("test", "1.0.0").AddDocker(new DockerOptions { AllowExec = true });

        Assert.Equal(["docker_exec", "docker_inspect", "docker_logs", "docker_ps"], builder.Tools.Select(t => t.Name).Order());
    }

    [Fact]
    public void AddDocker_NullOptions_ThrowsArgumentNullException()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Throws<ArgumentNullException>(() => builder.AddDocker((DockerOptions)null!));
    }

    [Fact]
    public void AddDocker_ReturnsSameBuilder()
    {
        var builder = new McpServerBuilder("test", "1.0.0");

        Assert.Same(builder, builder.AddDocker(new DockerOptions { AllowExec = true }));
    }
}
