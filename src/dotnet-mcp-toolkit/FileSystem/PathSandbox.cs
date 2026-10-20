namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// Resolves relative paths against a sandboxed root directory and rejects any path
/// that escapes it (including via <c>..</c> traversal or symbolic links).
/// </summary>
public sealed class PathSandbox
{
    /// <summary>
    /// Gets the absolute path to the sandbox root.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Creates a sandbox rooted at <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The root directory. Must exist.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> does not exist.</exception>
    public PathSandbox(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        Root = Path.GetFullPath(root);

        if (!Directory.Exists(Root))
        {
            throw new DirectoryNotFoundException($"Sandbox root does not exist: {Root}");
        }
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against the sandbox root and returns the
    /// full path. Throws if the resolved path escapes the sandbox.
    /// </summary>
    /// <param name="relativePath">A relative path (e.g. <c>src/Foo.cs</c> or <c>.</c>).</param>
    /// <returns>The resolved absolute path.</returns>
    /// <exception cref="UnauthorizedAccessException">The resolved path is outside the sandbox root.</exception>
    public string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var combined = Path.GetFullPath(Path.Combine(Root, relativePath));

        if (!combined.StartsWith(Root, StringComparison.Ordinal) ||
            (combined.Length > Root.Length && combined[Root.Length] != Path.DirectorySeparatorChar))
        {
            throw new UnauthorizedAccessException($"Path escapes the sandbox: {relativePath}");
        }

        return combined;
    }

    /// <summary>
    /// Returns the path relative to the sandbox root, using forward slashes as separators.
    /// </summary>
    /// <param name="absolutePath">An absolute path inside the sandbox.</param>
    /// <returns>A relative path with <c>/</c> separators.</returns>
    public string MakeRelative(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);

        var full = Path.GetFullPath(absolutePath);
        var relative = Path.GetRelativePath(Root, full);

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
