using System.Text.Json;
using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

/// <summary>
/// Bad arguments from the model must come back as error results that name the offending argument,
/// never as exceptions. Every case here is rejected before anything is written to the tree.
/// </summary>
public sealed class FileSystemToolsGuardTests : IClassFixture<TempTreeFixture>
{
    private readonly TempTreeFixture _tree;

    public FileSystemToolsGuardTests(TempTreeFixture tree)
    {
        _tree = tree;
    }

    [Fact]
    public async Task ReadFile_MissingPath_ReturnsErrorNamingPath()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("{}"));

        AssertErrorNames(result, "path");
    }

    [Fact]
    public async Task ReadFile_PathAsNumber_ReturnsErrorNamingPath()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""{ "path": 5 }"""));

        AssertErrorNames(result, "path");
    }

    [Fact]
    public async Task ReadFile_LimitZero_ReturnsErrorNamingLimit()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""{ "path": "readme.md", "limit": 0 }"""));

        AssertErrorNames(result, "limit");
    }

    [Fact]
    public async Task ReadFile_LimitAboveMaximum_ReturnsErrorNamingLimit()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""{ "path": "readme.md", "limit": 6000 }"""));

        AssertErrorNames(result, "limit");
    }

    [Fact]
    public async Task ReadFile_OffsetZero_ReturnsErrorNamingOffset()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""{ "path": "readme.md", "offset": 0 }"""));

        AssertErrorNames(result, "offset");
    }

    [Fact]
    public async Task Search_NegativeMaxResults_ReturnsErrorNamingMaxResults()
    {
        var result = await new FsSearchTool(Sandbox()).InvokeAsync(Args("""{ "pattern": ".", "maxResults": -1 }"""));

        AssertErrorNames(result, "maxResults");
    }

    [Fact]
    public async Task Search_GlobWithParentTraversal_ReturnsErrorNamingGlob()
    {
        var result = await new FsSearchTool(Sandbox()).InvokeAsync(Args("""{ "pattern": ".", "glob": "../*.cs" }"""));

        AssertErrorNames(result, "glob");
    }

    [Fact]
    public async Task Search_PatternLongerThanMaximum_ReturnsErrorNamingPattern()
    {
        var pattern = new string('a', 1001);

        var result = await new FsSearchTool(Sandbox()).InvokeAsync(Args($$"""{ "pattern": "{{pattern}}" }"""));

        AssertErrorNames(result, "pattern");
    }

    [Fact]
    public async Task Write_ContentAsInteger_ReturnsErrorNamingContent()
    {
        var result = await new FsWriteFileTool(Sandbox()).InvokeAsync(Args("""{ "path": "new.txt", "content": 42 }"""));

        AssertErrorNames(result, "content");
    }

    [Theory]
    [InlineData("dir/")]
    [InlineData("dir\\")]
    public async Task Write_PathWithTrailingSeparator_ReturnsErrorNamingPath(string path)
    {
        var result = await new FsWriteFileTool(Sandbox()).InvokeAsync(
            Args($$"""{ "path": "{{path.Replace("\\", "\\\\")}}", "content": "x" }"""));

        AssertErrorNames(result, "path");
    }

    [Fact]
    public async Task Write_ParentIsAFile_ReturnsErrorInsteadOfThrowing()
    {
        var result = await new FsWriteFileTool(Sandbox()).InvokeAsync(
            Args("""{ "path": "readme.md/sub/a.txt", "content": "x" }"""));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ReadFile_PathWithNulCharacter_ReturnsErrorNamingPath()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""{ "path": "readme.md\u0000" }"""));

        Assert.True(result.IsError);
        Assert.Contains("path contains invalid characters", result.Content[0].Text!);
    }

    [Fact]
    public async Task List_RecursiveAsString_ReturnsErrorNamingRecursive()
    {
        var result = await new FsListTool(Sandbox()).InvokeAsync(Args("""{ "recursive": "yes" }"""));

        AssertErrorNames(result, "recursive");
    }

    [Fact]
    public async Task List_ArgumentsAsJsonArray_ReturnsErrorNamingArguments()
    {
        var result = await new FsListTool(Sandbox()).InvokeAsync(Args("""["."]"""));

        Assert.True(result.IsError);
        Assert.Contains("arguments", result.Content[0].Text!);
    }

    [Fact]
    public async Task ReadFile_ArgumentsAsJsonArray_ReturnsErrorNamingArguments()
    {
        var result = await new FsReadFileTool(Sandbox()).InvokeAsync(Args("""["readme.md"]"""));

        Assert.True(result.IsError);
        Assert.Contains("arguments", result.Content[0].Text!);
    }

    [Fact]
    public void ReadFileConstructor_NullSandbox_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new FsReadFileTool(null!));

        Assert.Equal("sandbox", exception.ParamName);
    }

    [Fact]
    public void WriteFileConstructor_NullSandbox_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new FsWriteFileTool(null!));

        Assert.Equal("sandbox", exception.ParamName);
    }

    [Fact]
    public void SearchConstructor_NullSandbox_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new FsSearchTool(null!));

        Assert.Equal("sandbox", exception.ParamName);
    }

    [Fact]
    public void ListConstructor_NullSandbox_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new FsListTool(null!));

        Assert.Equal("sandbox", exception.ParamName);
    }

    private PathSandbox Sandbox() => new(_tree.Root);

    private static void AssertErrorNames(McpToolResult result, string argumentName)
    {
        Assert.True(result.IsError);
        Assert.Contains($"'{argumentName}'", result.Content[0].Text!);
    }

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
