using System.Text.Json;

namespace DotnetMcpToolkit.Tests.TestDoubles;

/// <summary>
/// Tool whose behaviour is supplied by a delegate; records the arguments of the last invocation.
/// </summary>
internal sealed class FakeTool : IMcpTool
{
    private const string _schema = """{"type":"object","properties":{"x":{"type":"integer"}}}""";

    private readonly Func<JsonElement?, McpToolResult> _handler;

    /// <summary>
    /// Creates a fake tool.
    /// </summary>
    /// <param name="name">Tool name advertised to clients.</param>
    /// <param name="handler">Delegate invoked for every call.</param>
    /// <param name="description">Tool description advertised to clients.</param>
    public FakeTool(string name, Func<JsonElement?, McpToolResult> handler, string description = "Fake tool")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);

        Name = name;
        Description = description;
        _handler = handler;

        using var document = JsonDocument.Parse(_schema);
        InputSchema = document.RootElement.Clone();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public JsonElement InputSchema { get; }

    /// <summary>
    /// Arguments passed to the most recent invocation, or <c>null</c> if none was supplied.
    /// </summary>
    public JsonElement? LastArguments { get; private set; }

    /// <summary>
    /// Number of times the tool has been invoked.
    /// </summary>
    public int InvocationCount { get; private set; }

    /// <inheritdoc />
    public Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastArguments = arguments;
        InvocationCount++;
        return Task.FromResult(_handler(arguments));
    }
}
