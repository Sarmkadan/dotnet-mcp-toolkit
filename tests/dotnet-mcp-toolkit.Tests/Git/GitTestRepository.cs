using System.Diagnostics;

namespace DotnetMcpToolkit.Tests.Git;

/// <summary>
/// A throwaway git repository under the temp directory, deleted on dispose.
/// </summary>
public sealed class GitTestRepository : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "gittools-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates an empty repository on branch <c>main</c> with a local identity.
    /// </summary>
    public GitTestRepository()
    {
        Root = Path.Combine(_base, "work");
        Directory.CreateDirectory(Root);

        Run("init", "-q", "-b", "main");
        Run("config", "user.name", "Test Author");
        Run("config", "user.email", "test@example.com");
        Run("config", "commit.gpgsign", "false");
    }

    /// <summary>
    /// Gets the working tree of the repository.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets a path for a bare repository that can act as a remote. The caller creates it.
    /// </summary>
    public string BareRemote => Path.Combine(_base, "remote.git");

    /// <summary>
    /// Writes a file relative to <see cref="Root"/>, creating directories as needed.
    /// </summary>
    /// <param name="relativePath">Path of the file inside the repository.</param>
    /// <param name="content">File content.</param>
    public void WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Stages every change and commits it.
    /// </summary>
    /// <param name="message">Commit subject.</param>
    public void CommitAll(string message)
    {
        Run("add", "-A");
        Run("commit", "-q", "-m", message);
    }

    /// <summary>
    /// Runs git in <see cref="Root"/> and returns its standard output.
    /// </summary>
    /// <param name="arguments">Arguments passed to git.</param>
    /// <returns>The captured standard output.</returns>
    /// <exception cref="InvalidOperationException">git exited with a non-zero code.</exception>
    public string Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_base, recursive: true);
    }
}
