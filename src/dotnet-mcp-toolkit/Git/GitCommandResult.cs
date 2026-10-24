namespace DotnetMcpToolkit.Git;

/// <summary>
/// The outcome of a single <c>git</c> invocation.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The captured standard output, truncated when it exceeded the capture limit.</param>
/// <param name="StandardError">The captured standard error, truncated when it exceeded the capture limit.</param>
public sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Gets a value indicating whether the command exited with code <c>0</c>.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}
