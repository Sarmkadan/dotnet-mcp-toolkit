using System.Text.Json;

namespace DotnetMcpToolkit.Protocol;

/// <summary>
/// Helpers for building, serializing and parsing JSON-RPC 2.0 messages.
/// </summary>
public static class JsonRpcMessages
{
    private const string _version = "2.0";

    // JSON-RPC 2.0 requires an explicit "id": null when the request id is unknown; a missing id member is not valid.
    private static readonly JsonElement _nullId = JsonSerializer.SerializeToElement<string?>(null);

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    /// <param name="id">Identifier of the request being answered.</param>
    /// <param name="result">Result payload.</param>
    /// <returns>A response with <c>result</c> set and no <c>error</c>.</returns>
    public static JsonRpcResponse Success(JsonElement? id, JsonElement result)
    {
        return new JsonRpcResponse(_version, id, result);
    }

    /// <summary>
    /// Creates a failed response.
    /// </summary>
    /// <param name="id">Identifier of the request being answered, or <see langword="null"/> if unknown.</param>
    /// <param name="code">Error code; see <see cref="JsonRpcErrorCodes"/>.</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="data">Optional structured error details.</param>
    /// <returns>A response with <c>error</c> set and no <c>result</c>.</returns>
    public static JsonRpcResponse Failure(JsonElement? id, int code, string message, JsonElement? data = null)
    {
        return new JsonRpcResponse(_version, id ?? _nullId, Error: new JsonRpcError(code, message, data));
    }

    /// <summary>
    /// Serializes a response to a single-line JSON string suitable for a newline-delimited transport.
    /// </summary>
    /// <param name="response">The response to serialize.</param>
    /// <returns>The JSON text, with null members omitted.</returns>
    public static string Serialize(JsonRpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.Serialize(response, JsonRpcSerializerContext.Default.JsonRpcResponse);
    }

    /// <summary>
    /// Parses and validates a JSON-RPC request or notification.
    /// </summary>
    /// <param name="json">The raw JSON text received from the client.</param>
    /// <param name="request">The parsed request when successful; otherwise <see langword="null"/>.</param>
    /// <param name="error">
    /// <see cref="JsonRpcErrorCodes.ParseError"/> when <paramref name="json"/> is malformed;
    /// <see cref="JsonRpcErrorCodes.InvalidRequest"/> when it is not a valid request object; otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when the message is a valid request; otherwise <see langword="false"/>.</returns>
    public static bool TryParseRequest(string json, out JsonRpcRequest? request, out JsonRpcError? error)
    {
        ArgumentNullException.ThrowIfNull(json);

        request = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = new JsonRpcError(JsonRpcErrorCodes.ParseError, "Parse error");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != _version ||
                !root.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(methodElement.GetString()))
            {
                error = new JsonRpcError(JsonRpcErrorCodes.InvalidRequest, "Invalid Request");
                return false;
            }

            // Elements are only valid while the document is alive, so copy them before it is disposed.
            JsonElement? id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null
                ? idElement.Clone()
                : null;

            JsonElement? parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : null;

            request = new JsonRpcRequest(_version, id, methodElement.GetString()!, parameters);
            return true;
        }
    }
}
