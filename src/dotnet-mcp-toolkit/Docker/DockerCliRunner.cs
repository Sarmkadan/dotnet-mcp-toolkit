using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// Runs the <c>docker</c> CLI and captures its output. Arguments are passed as an argument list
/// (never through a shell). The library does not link a Docker client: all daemon access goes through the CLI.
/// </summary>
public sealed class DockerCliRunner
{
    private const int _maxCapturedBytes = 4 * 1024 * 1024;
    private const int _bufferSize = 81920;
    private const string _truncationMarker = "\n[truncated]";
    private const int _exitCodeNotFound = 127;
    private const int _exitCodeTimeout = 124;

    private static readonly TimeSpan _fallbackTimeout = TimeSpan.FromSeconds(60);

    private readonly string? _dockerHost;
    private readonly string _dockerExecutable;
    private readonly TimeSpan _defaultTimeout;

    /// <summary>
    /// Creates a runner for the docker CLI.
    /// </summary>
    /// <param name="dockerHost">
    /// Daemon endpoint passed as <c>--host</c>, e.g. <c>unix:///var/run/docker.sock</c>.
    /// <see langword="null"/> or empty uses the CLI default (<c>DOCKER_HOST</c> or the local socket).
    /// </param>
    /// <param name="dockerExecutable">The docker executable to launch. Defaults to <c>docker</c> on <c>PATH</c>.</param>
    /// <param name="defaultTimeout">Run time limit used when <see cref="RunAsync"/> is not given one. Defaults to 60 seconds.</param>
    /// <exception cref="ArgumentException"><paramref name="dockerExecutable"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultTimeout"/> is zero or negative.</exception>
    public DockerCliRunner(string? dockerHost = null, string dockerExecutable = "docker", TimeSpan? defaultTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerExecutable);

        var timeout = defaultTimeout ?? _fallbackTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        _dockerHost = string.IsNullOrWhiteSpace(dockerHost) ? null : dockerHost;
        _dockerExecutable = dockerExecutable;
        _defaultTimeout = timeout;
    }

    /// <summary>
    /// Runs <c>docker</c> with <paramref name="arguments"/> and waits for it to exit.
    /// </summary>
    /// <param name="arguments">The arguments passed to docker, for example <c>["ps", "--format", "{{json .}}"]</c>.</param>
    /// <param name="ct">A token that kills the docker process tree when cancelled. Cancellation is rethrown to the caller.</param>
    /// <param name="timeout">Maximum run time before the process tree is killed. Defaults to the runner's default timeout.</param>
    /// <returns>
    /// The exit code and the captured output. A non-zero exit code is not an exception. A missing executable
    /// yields exit code <c>127</c>; a timeout yields exit code <c>124</c> with a note appended to standard error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> or one of its entries is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled; the docker process tree is killed first.</exception>
    public async Task<DockerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var effectiveTimeout = timeout ?? _defaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }

        var startInfo = CreateStartInfo(arguments);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return new DockerCommandResult(_exitCodeNotFound, string.Empty, "docker executable not found");
        }

        // Both pipes are drained while the process runs; waiting on one before reading the other can deadlock.
        // The readers take no token: they finish on EOF, which the kill below guarantees.
        var stdoutTask = ReadCappedAsync(process.StandardOutput.BaseStream);
        var stderrTask = ReadCappedAsync(process.StandardError.BaseStream);

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);

            var partialOutput = await stdoutTask;
            var partialError = await stderrTask;

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            var timeoutNote = $"docker command timed out after {effectiveTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds";

            return new DockerCommandResult(
                _exitCodeTimeout,
                partialOutput,
                string.IsNullOrEmpty(partialError) ? timeoutNote : $"{partialError}\n{timeoutNote}");
        }

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;

        return new DockerCommandResult(process.ExitCode, standardOutput, standardError);
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _dockerExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_dockerHost is not null)
        {
            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add(_dockerHost);
        }

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);

            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// Reads <paramref name="stream"/> to its end, keeping at most <see cref="_maxCapturedBytes"/> bytes.
    /// The remainder is read and discarded so the child process never blocks on a full pipe.
    /// </summary>
    private static async Task<string> ReadCappedAsync(Stream stream)
    {
        using var captured = new MemoryStream();
        var buffer = new byte[_bufferSize];
        var truncated = false;

        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
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
