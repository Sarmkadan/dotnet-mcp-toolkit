using DotnetMcpToolkit.FileSystem;

namespace DotnetMcpToolkit.Tests.FileSystem;

public sealed class PathSandboxTests : IDisposable
{
    private readonly SandboxFixture _fixture = new();

    public void Dispose()
    {
        _fixture.Dispose();
    }

    [Fact]
    public void Constructor_NonExistentRoot_ThrowsDirectoryNotFoundException()
    {
        var missing = Path.Combine(_fixture.Base, "missing");

        Assert.Throws<DirectoryNotFoundException>(() => new PathSandbox(missing));
    }

    [Fact]
    public void Constructor_Whitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new PathSandbox("   "));
    }

    [Fact]
    public void Resolve_RelativeFileInsideRoot_ReturnsFullPath()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var resolved = sandbox.Resolve(Path.Combine("a", "b.txt"));

        Assert.Equal(Path.Combine(sandbox.Root, "a", "b.txt"), resolved);
    }

    [Fact]
    public void Resolve_DotPath_ReturnsRoot()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var resolved = sandbox.Resolve(".");

        Assert.Equal(sandbox.Root, resolved);
    }

    [Fact]
    public void Resolve_ParentTraversal_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine("..", "outside", "secret.txt")));
    }

    [Fact]
    public void Resolve_NestedTraversalBackInside_IsAllowed()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var resolved = sandbox.Resolve(Path.Combine("a", "..", "a", "b.txt"));

        Assert.Equal(Path.Combine(sandbox.Root, "a", "b.txt"), resolved);
    }

    [Fact]
    public void Resolve_AbsolutePathInsideRoot_IsAllowed()
    {
        var sandbox = new PathSandbox(_fixture.Root);
        var absolute = Path.Combine(sandbox.Root, "a", "b.txt");

        var resolved = sandbox.Resolve(absolute);

        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void Resolve_AbsolutePathOutsideRoot_ThrowsUnauthorizedAccessException()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine(_fixture.Outside, "secret.txt")));
    }

    [Fact]
    public void Resolve_RootPrefixTrick_ThrowsUnauthorizedAccessException()
    {
        // "root2" starts with "root" as a string, but it is a different directory and must not pass the containment check.
        var sandbox = new PathSandbox(_fixture.Root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine("..", "root2", "file.txt")));
    }

    [Fact]
    public void Resolve_SymlinkPointingOutside_ThrowsUnauthorizedAccessException()
    {
        if (!_fixture.SymlinksSupported)
        {
            // Symlinks could not be created on this (non-Linux) platform, so there is nothing to check.
            return;
        }

        var sandbox = new PathSandbox(_fixture.Root);

        Assert.Throws<UnauthorizedAccessException>(() => sandbox.Resolve(Path.Combine("link-out", "secret.txt")));
    }

    [Fact]
    public void Resolve_SymlinkPointingInside_IsAllowed()
    {
        if (!_fixture.SymlinksSupported)
        {
            // Symlinks could not be created on this (non-Linux) platform, so there is nothing to check.
            return;
        }

        var sandbox = new PathSandbox(_fixture.Root);

        var resolved = sandbox.Resolve(Path.Combine("link-in", "b.txt"));

        Assert.Equal(Path.Combine(sandbox.Root, "link-in", "b.txt"), resolved);
    }

    [Fact]
    public void Resolve_NonExistentFileInsideRoot_IsAllowed()
    {
        // Write tools need to resolve paths that do not exist yet.
        var sandbox = new PathSandbox(_fixture.Root);

        var resolved = sandbox.Resolve(Path.Combine("a", "new.txt"));

        Assert.Equal(Path.Combine(sandbox.Root, "a", "new.txt"), resolved);
    }

    [Fact]
    public void TryResolve_Escape_ReturnsFalse()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var ok = sandbox.TryResolve(Path.Combine("..", "outside", "secret.txt"), out var fullPath);

        Assert.False(ok);
        Assert.Null(fullPath);
    }

    [Fact]
    public void TryResolve_Valid_ReturnsTrueAndPath()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var ok = sandbox.TryResolve(Path.Combine("a", "b.txt"), out var fullPath);

        Assert.True(ok);
        Assert.Equal(Path.Combine(sandbox.Root, "a", "b.txt"), fullPath);
    }

    [Fact]
    public void ToRelative_NestedPath_UsesForwardSlashes()
    {
        var sandbox = new PathSandbox(_fixture.Root);

        var relative = sandbox.ToRelative(Path.Combine(sandbox.Root, "a", "b.txt"));

        Assert.Equal("a/b.txt", relative);
    }
}
