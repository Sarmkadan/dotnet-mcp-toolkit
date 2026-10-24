namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// Confines filesystem tool operations to a root directory. Paths are resolved lexically
/// (<c>..</c> segments are normalized) and every existing segment is checked for symbolic
/// links that point outside the root.
/// </summary>
public sealed class PathSandbox
{
    private const int _maxLinkDepth = 40;

    private readonly string _prefix;

    /// <summary>
    /// Gets the absolute path to the sandbox root, without a trailing separator
    /// (except for a filesystem root such as <c>/</c>).
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Creates a sandbox rooted at <paramref name="root"/>.
    /// </summary>
    /// <param name="root">The root directory. Must exist. May itself be a symbolic link.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> does not exist.</exception>
    public PathSandbox(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var full = Path.GetFullPath(root);

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Sandbox root does not exist: {full}");
        }

        if (Directory.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
        {
            full = target.FullName;
        }

        Root = TrimTrailingSeparator(full);
        _prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Resolves <paramref name="relativeOrAbsolutePath"/> against the sandbox root and returns the
    /// full path. Absolute paths are accepted only when they lie inside the root.
    /// </summary>
    /// <param name="relativeOrAbsolutePath">A relative path (e.g. <c>src/Foo.cs</c> or <c>.</c>) or an absolute path inside the root.</param>
    /// <returns>The resolved absolute path inside the root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativeOrAbsolutePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The path escapes the sandbox root, directly or through a symbolic link.</exception>
    public string Resolve(string relativeOrAbsolutePath)
    {
        ArgumentNullException.ThrowIfNull(relativeOrAbsolutePath);

        var combined = Path.GetFullPath(Path.Combine(Root, relativeOrAbsolutePath));

        if (!IsInside(combined))
        {
            throw new UnauthorizedAccessException("Path escapes sandbox root");
        }

        EnsureNoEscapingLinks(combined, depth: 0);

        return combined;
    }

    /// <summary>
    /// Attempts to resolve <paramref name="path"/> the same way as <see cref="Resolve"/>.
    /// </summary>
    /// <param name="path">A relative path or an absolute path inside the root.</param>
    /// <param name="fullPath">The resolved absolute path when the method returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the path is inside the sandbox; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public bool TryResolve(string path, out string fullPath)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            fullPath = Resolve(path);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException or IOException)
        {
            fullPath = null!;
            return false;
        }
    }

    /// <summary>
    /// Returns the path relative to the sandbox root, using forward slashes as separators.
    /// </summary>
    /// <param name="fullPath">An absolute path inside the sandbox.</param>
    /// <returns>A relative path with <c>/</c> separators.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fullPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnauthorizedAccessException"><paramref name="fullPath"/> is outside the sandbox root.</exception>
    public string ToRelative(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        var full = Path.GetFullPath(fullPath);

        if (!IsInside(full))
        {
            throw new UnauthorizedAccessException("Path escapes sandbox root");
        }

        var relative = Path.GetRelativePath(Root, full);

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private bool IsInside(string fullPath) =>
        fullPath == Root || fullPath.StartsWith(_prefix, StringComparison.Ordinal);

    /// <summary>
    /// Walks each existing segment from the root down to <paramref name="fullPath"/>. When a segment is
    /// a symbolic link, its final target is resolved and checked for containment. The check is repeated
    /// on the target so that links reached through other links are also covered.
    /// </summary>
    private void EnsureNoEscapingLinks(string fullPath, int depth)
    {
        if (depth > _maxLinkDepth)
        {
            throw new UnauthorizedAccessException("Path escapes sandbox root");
        }

        var current = Root;
        var remainder = Path.GetRelativePath(Root, fullPath);

        if (remainder == ".")
        {
            return;
        }

        foreach (var segment in remainder.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (!info.Exists)
            {
                // Nothing below a missing segment can exist yet, so there is nothing more to check.
                return;
            }

            if (info.LinkTarget is null)
            {
                continue;
            }

            FileSystemInfo? target;
            try
            {
                target = info.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (IOException)
            {
                // Circular or unreadable links are treated as escapes (fail closed).
                throw new UnauthorizedAccessException("Path escapes sandbox root");
            }

            if (target is null)
            {
                // Dangling link: its destination cannot be verified, so reject it.
                throw new UnauthorizedAccessException("Path escapes sandbox root");
            }

            if (!IsInside(target.FullName))
            {
                throw new UnauthorizedAccessException("Path escapes sandbox root");
            }

            EnsureNoEscapingLinks(target.FullName, depth + 1);
        }
    }

    private static string TrimTrailingSeparator(string path)
    {
        var rootLength = Path.GetPathRoot(path)?.Length ?? 0;

        return path.Length > rootLength
            ? path.TrimEnd(Path.DirectorySeparatorChar)
            : path;
    }
}
