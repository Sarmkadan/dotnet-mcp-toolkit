using DotnetMcpToolkit.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotnetMcpToolkit.DependencyInjection;

/// <summary>
/// Registers an <see cref="IMcpServer"/> and its <see cref="IMcpTransport"/> in an <see cref="IServiceCollection"/>,
/// so a Generic Host or Worker Service can host the server. Run it with <see cref="McpServerRunner"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMcpServer"/> as a singleton. The server is built on first resolution from a new
    /// <see cref="McpServerBuilder"/> that <paramref name="configure"/> populates.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">Server name reported to the client.</param>
    /// <param name="version">Server version reported to the client.</param>
    /// <param name="configure">Adds the tool packs and custom tools to the builder.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="version"/> is null, empty or whitespace.</exception>
    public static IServiceCollection AddMcpServer(
        this IServiceCollection services,
        string name,
        string version,
        Action<McpServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        services.AddSingleton<IMcpServer>(_ =>
        {
            var builder = new McpServerBuilder(name, version);
            configure(builder);

            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IMcpServer"/> as a singleton, built from <see cref="McpServerOptions"/> bound to
    /// <paramref name="section"/> (typically <c>configuration.GetSection("Mcp")</c>). The options are validated
    /// on resolution and when the host starts.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="section">Configuration section holding the server options.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="section"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An invalid section throws when <see cref="IMcpServer"/> is resolved, or at host start: an
    /// <see cref="ArgumentException"/> or <see cref="ArgumentOutOfRangeException"/> names the offending property.
    /// </remarks>
    public static IServiceCollection AddMcpServer(this IServiceCollection services, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        // Configure + Bind from Microsoft.Extensions.Configuration.Binder keeps this package free of
        // Microsoft.Extensions.Options.ConfigurationExtensions.
        services.AddOptions<McpServerOptions>().Configure(options => section.Bind(options));

        return AddMcpServerFromOptions(services);
    }

    /// <summary>
    /// Registers <see cref="IMcpServer"/> as a singleton, built from <see cref="McpServerOptions"/> configured in code.
    /// The options are validated on resolution and when the host starts.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Sets the server options.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddMcpServer(this IServiceCollection services, Action<McpServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<McpServerOptions>().Configure(configure);

        return AddMcpServerFromOptions(services);
    }

    /// <summary>
    /// Registers <see cref="IMcpTransport"/> as a singleton <see cref="StdioTransport"/> over the process's
    /// standard input and output.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddStdioTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMcpTransport>(_ => new StdioTransport(Console.OpenStandardInput(), Console.OpenStandardOutput()));

        return services;
    }

    private static IServiceCollection AddMcpServerFromOptions(IServiceCollection services)
    {
        services.AddOptions<McpServerOptions>()
            .Validate(options =>
            {
                options.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton<IMcpServer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

            return McpServerBuilder.FromOptions(options).Build();
        });

        return services;
    }
}
