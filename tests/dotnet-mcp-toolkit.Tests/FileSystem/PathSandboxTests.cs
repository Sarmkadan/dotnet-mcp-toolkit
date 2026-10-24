using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class PathSandboxTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "pathsandbox-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _outside;

    public PathSandboxTests()
    {
        _root = Path.Combine(_base, "root");
        _outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "src", "Foo.cs"), "class Foo {}");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");
    }

    public void Dispose()
    {
        Directory.Delete(_base, recursive: true);
    }

    [Fact]
    public void Constructor_WhitespaceRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new PathSandbox("   "));
    }

    [Fact]
    public void Constructor_MissingRoot_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new PathSandbox(Path.Combine(_base, "missing")));
    }

    [Fact]
    public void Resolve_RelativeFile_ReturnsPathInsideRoot()
    {
        var sandbox = new PathSandbox(_root);

        var resolved = sandbox.Resolve(Path.Combine("src", "Foo.cs"));

        Assert.Equal(Path.Combine(sandbox.Root, "src", "Foo.cs"), resolved);
    }

    [Fact]
    public void Resolve_ParentTraversal_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine("..", "outside", "secret.txt")));
    }

    [Fact]
    public void Resolve_AbsolutePathOutsideRoot_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine(_outside, "secret.txt")));
    }

    [Fact]
    public void Resolve_SiblingPrefixDirectory_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_root);
        var sibling = _root + "-sibling";
        Directory.CreateDirectory(sibling);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(sibling));
        }
        finally
        {
            Directory.Delete(sibling);
        }
    }

    [Fact]
    public void Resolve_SymlinkToOutside_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_root);
        Directory.CreateSymbolicLink(Path.Combine(_root, "escape"), _outside);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine("escape", "secret.txt")));
    }

    [Fact]
    public void TryResolve_ParentTraversal_ReturnsFalse()
    {
        var sandbox = new PathSandbox(_root);

        var ok = sandbox.TryResolve(Path.Combine("..", "outside"), out var fullPath);

        Assert.False(ok);
        Assert.Null(fullPath);
    }

    [Fact]
    public void ToRelative_NestedFile_UsesForwardSlashes()
    {
        var sandbox = new PathSandbox(_root);

        var relative = sandbox.ToRelative(Path.Combine(sandbox.Root, "src", "Foo.cs"));

        Assert.Equal("src/Foo.cs", relative);
    }
}
