using System.Text.Json;

namespace DotnetMcpToolkit.Protocol;

/// <summary>
/// A JSON-RPC 2.0 response. Exactly one of <paramref name="Result"/> or <paramref name="Error"/> is expected to be set.
/// </summary>
/// <param name="Jsonrpc">Protocol version; always <c>"2.0"</c>.</param>
/// <param name="Id">Identifier of the request being answered, or <see langword="null"/> when it could not be determined.</param>
/// <param name="Result">Successful result payload.</param>
/// <param name="Error">Error object describing a failure.</param>
public sealed record JsonRpcResponse(
    string Jsonrpc = "2.0",
    JsonElement? Id = null,
    JsonElement? Result = null,
    JsonRpcError? Error = null);
