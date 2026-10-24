using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetMcpToolkit.Protocol;

/// <summary>
/// Source-generated JSON metadata for the JSON-RPC message types. Properties are camelCase and null values are omitted.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonElement))]
public partial class JsonRpcSerializerContext : JsonSerializerContext
{
}
