namespace DotnetMcpToolkit.DependencyInjection;

/// <summary>
/// Runs an <see cref="IMcpServer"/> over an <see cref="IMcpTransport"/>. It has no dependency on Hosting, so a
/// <c>BackgroundService</c> can call it in one line.
/// </summary>
/// <remarks>
/// <para>
/// Register the server and transport with the <see cref="ServiceCollectionExtensions"/> methods, then wrap the runner in a
/// <c>BackgroundService</c>:
/// </para>
/// <code>
/// services.AddSingleton&lt;McpServerRunner&gt;();
///
/// public sealed class McpWorker(McpServerRunner runner) : BackgroundService
/// {
///     protected override Task ExecuteAsync(CancellationToken stoppingToken) =&gt; runner.RunAsync(stoppingToken);
/// }
///
/// services.AddHostedService&lt;McpWorker&gt;();
/// </code>
/// <para>
/// The host stops the worker by cancelling <c>stoppingToken</c>, which ends the JSON-RPC loop.
/// The runner does not own the transport; the service provider disposes it.
/// </para>
/// </remarks>
public sealed class McpServerRunner(IMcpServer server, IMcpTransport transport)
{
    /// <summary>
    /// Runs the JSON-RPC message loop until the transport closes or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token that stops the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return server.RunAsync(transport, cancellationToken);
    }
}
