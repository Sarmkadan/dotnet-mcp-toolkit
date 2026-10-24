namespace DotnetMcpToolkit.Options;

/// <summary>
/// Configuration for the git tool pack. Binds from a configuration section such as <c>Mcp:Git</c>.
/// </summary>
public sealed class GitOptions
{
    /// <summary>
    /// Gets or sets the path to the working tree the tools operate on. Required.
    /// </summary>
    public string RepositoryPath { get; set; } = "";

    /// <summary>
    /// Gets or sets the git executable to launch. Defaults to <c>git</c> on <c>PATH</c>.
    /// </summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Gets or sets the largest number of commits <c>git_log</c> returns, from 1 to 500. Defaults to 500.
    /// </summary>
    public int MaxLogCount { get; set; } = 500;

    /// <summary>
    /// Checks the options.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="RepositoryPath"/> or <see cref="GitExecutable"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxLogCount"/> is outside 1-500.</exception>
    public void Validate()
    {
        OptionsValidation.RequireText(RepositoryPath, nameof(RepositoryPath));
        OptionsValidation.RequireText(GitExecutable, nameof(GitExecutable));
        OptionsValidation.RequireInRange(MaxLogCount, 1, 500, nameof(MaxLogCount));
    }
}
