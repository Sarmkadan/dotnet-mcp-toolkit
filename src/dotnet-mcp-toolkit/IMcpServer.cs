namespace DotnetMcpToolkit;

/// <summary>
/// A Model Context Protocol (MCP) server: a named collection of tools that an AI client
/// (Claude Desktop, Claude Code, GitHub Copilot, etc.) can discover and invoke over a transport.
/// </summary>
public interface IMcpServer
{
    /// <summary>
    /// Gets the server name reported to the client during the <c>initialize</c> handshake.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the server version reported to the client during the <c>initialize</c> handshake.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the tools this server exposes.
    /// </summary>
    IReadOnlyList<IMcpTool> Tools { get; }

    /// <summary>
    /// Runs the JSON-RPC message loop over the given transport until the transport closes
    /// or the token is cancelled.
    /// </summary>
    /// <param name="transport">The transport to read requests from and write responses to.</param>
    /// <param name="cancellationToken">Token that stops the loop.</param>
    Task RunAsync(IMcpTransport transport, CancellationToken cancellationToken = default);
}
