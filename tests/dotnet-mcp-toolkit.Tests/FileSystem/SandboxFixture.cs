namespace DotnetMcpToolkit.Tests.FileSystem;

/// <summary>
/// Builds a temporary tree for sandbox tests and deletes it on dispose:
/// <c>base/root/a/b.txt</c>, <c>base/root/a/sub/</c>, <c>base/outside/secret.txt</c>,
/// <c>base/root2/file.txt</c> (a sibling whose name starts with the root name), and inside the root
/// the symbolic links <c>link-out -&gt; ../outside</c> and <c>link-in -&gt; a</c>.
/// </summary>
internal sealed class SandboxFixture : IDisposable
{
    public SandboxFixture()
    {
        Base = Directory.CreateTempSubdirectory("sandbox").FullName;
        Root = Path.Combine(Base, "root");
        Outside = Path.Combine(Base, "outside");
        SiblingWithRootPrefix = Path.Combine(Base, "root2");

        Directory.CreateDirectory(Path.Combine(Root, "a", "sub"));
        File.WriteAllText(Path.Combine(Root, "a", "b.txt"), "hello");

        Directory.CreateDirectory(Outside);
        File.WriteAllText(Path.Combine(Outside, "secret.txt"), "secret");

        Directory.CreateDirectory(SiblingWithRootPrefix);
        File.WriteAllText(Path.Combine(SiblingWithRootPrefix, "file.txt"), "sibling");

        SymlinksSupported = TryCreateSymlinks();
    }

    /// <summary>Temporary directory that contains everything else.</summary>
    public string Base { get; }

    /// <summary>The sandbox root (<c>base/root</c>).</summary>
    public string Root { get; }

    /// <summary>A directory next to the root, outside the sandbox.</summary>
    public string Outside { get; }

    /// <summary>A sibling whose name starts with the root name (<c>base/root2</c>).</summary>
    public string SiblingWithRootPrefix { get; }

    /// <summary>
    /// <see langword="false"/> only on a non-Linux platform where symbolic links could not be created.
    /// On Linux the links must be created, otherwise the constructor throws and the tests fail.
    /// </summary>
    public bool SymlinksSupported { get; }

    public void Dispose()
    {
        Directory.Delete(Base, recursive: true);
    }

    private bool TryCreateSymlinks()
    {
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(Root, "link-out"), "../outside");
            Directory.CreateSymbolicLink(Path.Combine(Root, "link-in"), "a");
            return true;
        }
        catch (Exception) when (!OperatingSystem.IsLinux())
        {
            // Creating symlinks needs privileges on some Windows setups; the symlink tests return early there.
            return false;
        }
    }
}
