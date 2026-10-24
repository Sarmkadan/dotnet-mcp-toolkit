using System.Diagnostics;
using System.Text;

namespace DotnetMcpToolkit.Git;

/// <summary>
/// Runs <c>git</c> commands inside one repository and captures their output. Arguments are passed
/// as an argument list (never through a shell), and options that would execute arbitrary programs
/// or inject configuration are rejected.
/// </summary>
public sealed class GitCommandRunner
{
    private const int _maxCapturedBytes = 4 * 1024 * 1024;
    private const int _bufferSize = 81920;
    private const string _truncationMarker = "\n[truncated]";

    private readonly string _repositoryPath;
    private readonly string _gitExecutable;

    /// <summary>
    /// Creates a runner that executes git with the repository as its working directory.
    /// </summary>
    /// <param name="repositoryPath">The repository directory. Must exist.</param>
    /// <param name="gitExecutable">The git executable to launch. Defaults to <c>git</c> on <c>PATH</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> or <paramref name="gitExecutable"/> is empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="repositoryPath"/> is not an existing directory.</exception>
    public GitCommandRunner(string repositoryPath, string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);

        var full = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Repository directory does not exist: {full}");
        }

        _repositoryPath = full;
        _gitExecutable = gitExecutable;
    }

    /// <summary>
    /// Runs git with <paramref name="arguments"/> and waits for it to exit.
    /// </summary>
    /// <param name="arguments">The arguments passed to git, for example <c>["status", "--porcelain"]</c>.</param>
    /// <param name="ct">A token that kills the git process tree when cancelled.</param>
    /// <returns>The exit code and the captured output. A non-zero exit code is not an exception.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> or one of its entries is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="arguments"/> contains an entry that starts with <c>--upload-pack</c> or <c>--exec</c>,
    /// or a <c>-c core.</c> configuration override.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; the git process tree is killed first.</exception>
    public async Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ValidateArguments(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            WorkingDirectory = _repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["GIT_PAGER"] = "cat";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Both pipes are drained while the process runs; waiting on one before reading the other can deadlock.
        var stdoutTask = ReadCappedAsync(process.StandardOutput.BaseStream, ct);
        var stderrTask = ReadCappedAsync(process.StandardError.BaseStream, ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;

        return new GitCommandResult(process.ExitCode, standardOutput, standardError);
    }

    /// <summary>
    /// Checks whether the repository directory is inside a git work tree.
    /// </summary>
    /// <param name="ct">A token that cancels the check.</param>
    /// <returns><see langword="true"/> when <c>git rev-parse --is-inside-work-tree</c> prints <c>true</c>; otherwise <see langword="false"/>.</returns>
    public async Task<bool> IsRepositoryAsync(CancellationToken ct)
    {
        var result = await RunAsync(["rev-parse", "--is-inside-work-tree"], ct);

        return result.Succeeded && result.StandardOutput.Trim() == "true";
    }

    private static void ValidateArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];

            ArgumentNullException.ThrowIfNull(argument);

            if (IsForbidden(argument) || IsSplitConfigOverride(arguments, i))
            {
                throw new ArgumentException($"Git argument is not allowed: {argument}", nameof(arguments));
            }
        }
    }

    private static bool IsForbidden(string argument) =>
        argument.StartsWith("--upload-pack", StringComparison.OrdinalIgnoreCase)
        || argument.StartsWith("--exec", StringComparison.OrdinalIgnoreCase)
        || argument.Contains("-c core.", StringComparison.OrdinalIgnoreCase);

    // "-c" "core.pager=..." is the same configuration override as "-c core.pager=...", written as two arguments.
    private static bool IsSplitConfigOverride(IReadOnlyList<string> arguments, int index) =>
        arguments[index].Equals("-c", StringComparison.OrdinalIgnoreCase)
        && index + 1 < arguments.Count
        && arguments[index + 1].StartsWith("core.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <paramref name="stream"/> to its end, keeping at most <see cref="_maxCapturedBytes"/> bytes.
    /// The remainder is read and discarded so the child process never blocks on a full pipe.
    /// </summary>
    private static async Task<string> ReadCappedAsync(Stream stream, CancellationToken ct)
    {
        using var captured = new MemoryStream();
        var buffer = new byte[_bufferSize];
        var truncated = false;

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var room = _maxCapturedBytes - captured.Length;

            if (read > room)
            {
                truncated = true;
            }

            if (room > 0)
            {
                captured.Write(buffer, 0, (int)Math.Min(room, read));
            }
        }

        var text = Encoding.UTF8.GetString(captured.GetBuffer(), 0, (int)captured.Length);

        return truncated ? text + _truncationMarker : text;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited on its own before the kill was delivered.
        }
    }
}
