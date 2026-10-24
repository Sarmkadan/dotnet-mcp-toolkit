using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetMcpToolkit.Protocol;

/// <summary>
/// A JSON-RPC 2.0 request or notification. A message without an <c>id</c> is a notification and expects no response.
/// </summary>
/// <param name="Jsonrpc">Protocol version; always <c>"2.0"</c>.</param>
/// <param name="Id">Request identifier (string or number), or <see langword="null"/> for a notification.</param>
/// <param name="Method">Name of the method to invoke.</param>
/// <param name="Params">Method parameters (object or array), or <see langword="null"/> when omitted.</param>
public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);
