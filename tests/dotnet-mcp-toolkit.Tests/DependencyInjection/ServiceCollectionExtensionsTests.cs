using DotnetMcpToolkit.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotnetMcpToolkit.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "di-fs-" + Guid.NewGuid().ToString("N"));

    public ServiceCollectionExtensionsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AddMcpServer_WithConfigureDelegate_ResolvesServerWithTools()
    {
        var services = new ServiceCollection();
        services.AddMcpServer("di-test", "2.0.0", builder => builder.AddFileSystem(_root, readOnly: true));
        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<IMcpServer>();

        Assert.Equal("di-test", server.Name);
        Assert.Equal("2.0.0", server.Version);
        Assert.Contains(server.Tools, tool => tool.Name == "fs_list");
    }

    [Fact]
    public async Task AddMcpServer_WithConfiguration_BindsNameVersionAndFileSystem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Name"] = "from-config",
                ["Mcp:Version"] = "3.1.4",
                ["Mcp:FileSystem:Root"] = _root,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMcpServer(configuration.GetSection("Mcp"));
        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<IMcpServer>();

        Assert.Equal("from-config", server.Name);
        Assert.Equal("3.1.4", server.Version);
        Assert.Contains(server.Tools, tool => tool.Name == "fs_list");
    }

    [Fact]
    public async Task AddMcpServer_WithInvalidConfiguration_ThrowsOnResolve()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Name"] = "missing-root",
                ["Mcp:FileSystem:ReadOnly"] = "true",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddMcpServer(configuration.GetSection("Mcp"));
        await using var provider = services.BuildServiceProvider();

        var exception = Record.Exception(() => provider.GetRequiredService<IMcpServer>());

        Assert.True(exception is OptionsValidationException or ArgumentException, $"Unexpected exception: {exception}");
    }

    [Fact]
    public async Task AddMcpServer_RegistersSingleton()
    {
        var services = new ServiceCollection();
        services.AddMcpServer("singleton", "1.0.0", builder => builder.AddFileSystem(_root));
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IMcpServer>();
        var second = provider.GetRequiredService<IMcpServer>();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddStdioTransport_ResolvesIMcpTransport()
    {
        var services = new ServiceCollection();
        services.AddStdioTransport();
        await using var provider = services.BuildServiceProvider();

        var transport = provider.GetRequiredService<IMcpTransport>();

        Assert.IsType<StdioTransport>(transport);
        Assert.Same(transport, provider.GetRequiredService<IMcpTransport>());
    }
}
