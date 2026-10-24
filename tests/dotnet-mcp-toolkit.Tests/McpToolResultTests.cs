using System.Text.Json;

namespace DotnetMcpToolkit.Tests;

public class McpToolResultTests
{
    // Json<T> writes camelCase names, so the reader must match names case-insensitively.
    private static readonly JsonSerializerOptions _caseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CountPayload(int RowCount, bool Truncated);

    private sealed record NullablePayload(string? Name, int Count);

    private sealed record Row(string Label, List<int> Values);

    private sealed record Table(string Name, List<Row> Rows);

    private sealed record DatePayload(DateTime At);

    [Fact]
    public void Text_WithPayload_ReturnsSingleTextBlockAndNotError()
    {
        var result = McpToolResult.Text("hello");

        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("hello", result.Content[0].Text);
        Assert.False(result.IsError);
    }

    [Fact]
    public void Text_WithEmptyString_ReturnsEmptyTextBlock()
    {
        var result = McpToolResult.Text(string.Empty);

        Assert.Single(result.Content);
        Assert.Equal(string.Empty, result.Content[0].Text);
    }

    [Fact]
    public void Error_WithMessage_SetsIsErrorTrue()
    {
        var result = McpToolResult.Error("boom");

        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal("boom", result.Content[0].Text);
    }

    [Fact]
    public void Json_WithRecord_SerializesCamelCase()
    {
        var result = McpToolResult.Json(new CountPayload(3, false));

        var text = result.Content[0].Text!;
        Assert.Contains("\"rowCount\":3", text);
        Assert.DoesNotContain("RowCount", text);
    }

    [Fact]
    public void Json_WithNullProperty_OmitsIt()
    {
        var result = McpToolResult.Json(new NullablePayload(null, 1));

        var text = result.Content[0].Text!;
        Assert.DoesNotContain("name", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"count\":1", text);
    }

    [Fact]
    public void Json_WithNestedCollections_RoundTrips()
    {
        var original = new Table("users", [new Row("a", [1, 2]), new Row("b", [])]);

        var result = McpToolResult.Json(original);
        var parsed = JsonSerializer.Deserialize<Table>(result.Content[0].Text!, _caseInsensitiveOptions);

        Assert.NotNull(parsed);
        Assert.Equal("users", parsed.Name);
        Assert.Equal(2, parsed.Rows.Count);
        Assert.Equal("a", parsed.Rows[0].Label);
        Assert.Equal([1, 2], parsed.Rows[0].Values);
        Assert.Equal("b", parsed.Rows[1].Label);
        Assert.Empty(parsed.Rows[1].Values);
    }

    [Fact]
    public void Json_WithDateTime_UsesIso8601()
    {
        var result = McpToolResult.Json(new DatePayload(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));

        Assert.Contains("\"2024-01-02T03:04:05", result.Content[0].Text!);
    }

    [Fact]
    public void Record_Equality_TwoResultsWithSameTextAreNotEqualBecauseContentListIsReferenceCompared()
    {
        // McpToolResult is a record, but Content is an IReadOnlyList and records compare collections by
        // reference. Each factory call builds a new list, so two results with identical text are not equal.
        // McpContent itself is a record and does compare structurally. Production semantics are left as-is.
        var first = McpToolResult.Text("a");
        var second = McpToolResult.Text("a");

        Assert.NotEqual(first, second);
        Assert.Equal(first.Content[0], second.Content[0]);
    }
}
