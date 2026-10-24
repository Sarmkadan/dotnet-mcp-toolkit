using System.Text.Json;

namespace DotnetMcpToolkit.Protocol;

/// <summary>
/// The error object carried by a failed JSON-RPC 2.0 response.
/// </summary>
/// <param name="Code">Numeric error code; see <see cref="JsonRpcErrorCodes"/>.</param>
/// <param name="Message">Short human-readable description of the error.</param>
/// <param name="Data">Optional structured details about the error.</param>
public sealed record JsonRpcError(int Code, string Message, JsonElement? Data = null);
