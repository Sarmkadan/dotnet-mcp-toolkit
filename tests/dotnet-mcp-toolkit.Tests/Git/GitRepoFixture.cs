using System.Diagnostics;

namespace DotnetMcpToolkit.Tests.Git;

/// <summary>
/// A git repository with two commits, an uncommitted change to <c>b.txt</c> and an untracked <c>c.txt</c>.
/// The directory lives under the temp directory and is deleted on dispose.
/// </summary>
/// <remarks>
/// History (oldest first):
/// <list type="bullet">
/// <item><c>feat: add a</c> adds <c>a.txt</c> with <c>one</c>, <c>two</c>, <c>three</c>.</item>
/// <item><c>fix: shout</c> changes line 2 of <c>a.txt</c> to <c>TWO</c> and adds <c>b.txt</c>.</item>
/// </list>
/// </remarks>
public sealed class GitRepoFixture : IDisposable
{
    /// <summary>
    /// Creates the repository and its history.
    /// </summary>
    public GitRepoFixture()
    {
        Root = Directory.CreateTempSubdirectory("gitrepo").FullName;

        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Test");
        Git("config", "commit.gpgsign", "false");

        WriteFile("a.txt", "one\ntwo\nthree\n");
        Git("add", "a.txt");
        Git("commit", "-q", "-m", "feat: add a");

        WriteFile("a.txt", "one\nTWO\nthree\n");
        WriteFile("b.txt", "bee\n");
        Git("add", "a.txt", "b.txt");
        Git("commit", "-q", "-m", "fix: shout");

        // Uncommitted modification of a tracked file and an untracked file.
        WriteFile("b.txt", "bee\nbuzz\n");
        WriteFile("c.txt", "untracked\n");
    }

    /// <summary>
    /// Gets the working tree of the repository.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Runs git in <see cref="Root"/> and returns its standard output.
    /// </summary>
    /// <param name="arguments">Arguments passed to git.</param>
    /// <returns>The captured standard output.</returns>
    /// <exception cref="InvalidOperationException">git exited with a non-zero code; the message carries stderr.</exception>
    public string Git(params string[] arguments)
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
        Directory.Delete(Root, recursive: true);
    }

    private void WriteFile(string relativePath, string content)
    {
        File.WriteAllText(Path.Combine(Root, relativePath), content);
    }
}
