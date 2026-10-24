using System.Text.Json;
using DotnetMcpToolkit.Protocol;

namespace DotnetMcpToolkit.Tests.Protocol;

public class JsonRpcMessagesTests
{
    [Fact]
    public void TryParseRequest_ValidRequest_ReturnsRequestWithIdAndParams()
    {
        var json = """{"jsonrpc":"2.0","id":7,"method":"tools/list","params":{"cursor":"x"}}""";

        var ok = JsonRpcMessages.TryParseRequest(json, out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("tools/list", request.Method);
        Assert.Equal(7, request.Id!.Value.GetInt32());
        Assert.Equal("x", request.Params!.Value.GetProperty("cursor").GetString());
    }

    [Fact]
    public void TryParseRequest_NoId_ReturnsNotificationWithNullId()
    {
        var json = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

        var ok = JsonRpcMessages.TryParseRequest(json, out var request, out _);

        Assert.True(ok);
        Assert.Null(request!.Id);
        Assert.Null(request.Params);
    }

    [Fact]
    public void TryParseRequest_MalformedJson_ReturnsParseError()
    {
        var ok = JsonRpcMessages.TryParseRequest("{not json", out var request, out var error);

        Assert.False(ok);
        Assert.Null(request);
        Assert.Equal(JsonRpcErrorCodes.ParseError, error!.Code);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"1.0","id":1,"method":"ping"}""")]
    [InlineData("""{"id":1,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":""}""")]
    [InlineData("""["not","an","object"]""")]
    public void TryParseRequest_InvalidRequestObject_ReturnsInvalidRequest(string json)
    {
        var ok = JsonRpcMessages.TryParseRequest(json, out var request, out var error);

        Assert.False(ok);
        Assert.Null(request);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, error!.Code);
    }

    [Fact]
    public void Serialize_SuccessResponse_OmitsErrorAndWritesSingleLine()
    {
        var id = JsonDocument.Parse("3").RootElement.Clone();
        var result = JsonDocument.Parse("""{"ok":true}""").RootElement.Clone();

        var json = JsonRpcMessages.Serialize(JsonRpcMessages.Success(id, result));

        Assert.Equal("""{"jsonrpc":"2.0","id":3,"result":{"ok":true}}""", json);
    }

    [Fact]
    public void Serialize_FailureResponse_OmitsResultAndWritesNullId()
    {
        var json = JsonRpcMessages.Serialize(
            JsonRpcMessages.Failure(null, JsonRpcErrorCodes.MethodNotFound, "Method not found"));

        Assert.Equal(
            """{"jsonrpc":"2.0","id":null,"error":{"code":-32601,"message":"Method not found"}}""",
            json);
    }
}
