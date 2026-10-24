using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class FsReadFileToolTests : IClassFixture<TempTreeFixture>
{
    private readonly FsReadFileTool _tool;

    public FsReadFileToolTests(TempTreeFixture tree)
    {
        _tool = new FsReadFileTool(new PathSandbox(tree.Root));
    }

    [Fact]
    public async Task InvokeAsync_ExistingFile_ReturnsContent()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "readme.md" }"""));

        Assert.False(result.IsError);
        Assert.Equal(string.Join('\n', TempTreeFixture.ReadmeLines), result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_OffsetAndLimit_ReturnsRequestedLines()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "src/Program.cs", "offset": 4, "limit": 1 }"""));

        Assert.False(result.IsError);
        Assert.Equal(TempTreeFixture.HelloLine, result.Content[0].Text);
    }

    [Fact]
    public async Task InvokeAsync_MissingPathArgument_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("{}"));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_BinaryFile_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "src/bin/app.dll" }"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_NonExistentFile_ReturnsError()
    {
        var result = await _tool.InvokeAsync(Args("""{ "path": "missing.txt" }"""));

        Assert.True(result.IsError);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
