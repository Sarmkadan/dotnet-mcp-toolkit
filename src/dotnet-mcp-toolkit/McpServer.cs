using System.Text.Json;
using DotnetMcpToolkit.Protocol;

namespace DotnetMcpToolkit;

/// <summary>
/// Default <see cref="IMcpServer"/>: answers the MCP lifecycle (<c>initialize</c>, <c>ping</c>),
/// tool discovery (<c>tools/list</c>) and tool execution (<c>tools/call</c>) over JSON-RPC.
/// </summary>
internal sealed class McpServer : IMcpServer
{
    private const string _protocolVersion = "2025-06-18";

    /// <summary>
    /// Creates a server with the given identity and tools.
    /// </summary>
    /// <param name="name">Server name reported to the client.</param>
    /// <param name="version">Server version reported to the client.</param>
    /// <param name="tools">Tools exposed by the server.</param>
    public McpServer(string name, string version, IReadOnlyList<IMcpTool> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(tools);

        Name = name;
        Version = version;
        Tools = tools;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <inheritdoc />
    public IReadOnlyList<IMcpTool> Tools { get; }

    /// <inheritdoc />
    public async Task RunAsync(IMcpTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);

        try
        {
            while (true)
            {
                var json = await transport.ReadMessageAsync(cancellationToken).ConfigureAwait(false);

                if (json is null)
                {
                    return;
                }

                if (!JsonRpcMessages.TryParseRequest(json, out var request, out var error))
                {
                    var failure = JsonRpcMessages.Failure(null, error!.Code, error.Message);
                    await transport.WriteMessageAsync(JsonRpcMessages.Serialize(failure), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var response = await DispatchAsync(request!, cancellationToken).ConfigureAwait(false);

                // Notifications (requests without an id) never receive a response.
                if (response is not null && request!.Id is not null)
                {
                    await transport.WriteMessageAsync(JsonRpcMessages.Serialize(response), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal way to stop the loop.
        }
    }

    private async Task<JsonRpcResponse?> DispatchAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var id = request.Id;

        switch (request.Method)
        {
            case "initialize":
                return JsonRpcMessages.Success(id, JsonSerializer.SerializeToElement(new
                {
                    protocolVersion = _protocolVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = Name, version = Version },
                }));

            case "ping":
                return JsonRpcMessages.Success(id, JsonSerializer.SerializeToElement(new { }));

            case "tools/list":
                return JsonRpcMessages.Success(id, JsonSerializer.SerializeToElement(new
                {
                    tools = Tools.Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        inputSchema = tool.InputSchema,
                    }),
                }));

            case "tools/call":
                return await CallToolAsync(request, cancellationToken).ConfigureAwait(false);

            default:
                if (request.Method.StartsWith("notifications/", StringComparison.Ordinal))
                {
                    return null;
                }

                return JsonRpcMessages.Failure(id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {request.Method}");
        }
    }

    private async Task<JsonRpcResponse> CallToolAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var id = request.Id;

        if (request.Params is not { ValueKind: JsonValueKind.Object } parameters ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String ||
            nameElement.GetString() is not { Length: > 0 } toolName)
        {
            return JsonRpcMessages.Failure(id, JsonRpcErrorCodes.InvalidParams, "Missing required parameter: name");
        }

        var tool = Tools.FirstOrDefault(t => t.Name == toolName);

        if (tool is null)
        {
            return JsonRpcMessages.Failure(id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {toolName}");
        }

        JsonElement? arguments = parameters.TryGetProperty("arguments", out var argumentsElement)
            ? argumentsElement
            : null;

        McpToolResult result;
        try
        {
            result = await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tool failures are domain errors for the model, not protocol errors.
            result = McpToolResult.Error(ex.Message);
        }

        return JsonRpcMessages.Success(id, JsonSerializer.SerializeToElement(new
        {
            content = result.Content.Select(block => new { type = block.Type, text = block.Text }),
            isError = result.IsError,
        }));
    }
}
