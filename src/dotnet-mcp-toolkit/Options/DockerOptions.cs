namespace DotnetMcpToolkit.Options;

/// <summary>
/// Configuration for the docker tool pack. Binds from a configuration section such as <c>Mcp:Docker</c>.
/// </summary>
public sealed class DockerOptions
{
    private static readonly string[] _dockerHostSchemes = ["unix://", "tcp://", "npipe://", "ssh://"];

    /// <summary>
    /// Gets or sets the daemon endpoint passed as <c>--host</c>, e.g. <c>unix:///var/run/docker.sock</c>. It must start
    /// with <c>unix://</c>, <c>tcp://</c>, <c>npipe://</c> or <c>ssh://</c>. <see langword="null"/> uses <c>DOCKER_HOST</c>
    /// or the CLI's default socket.
    /// </summary>
    public string? DockerHost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>docker_exec</c> is registered. It runs commands inside containers,
    /// so it is off by default.
    /// </summary>
    public bool AllowExec { get; set; }

    /// <summary>
    /// Gets or sets the docker executable to launch. Defaults to <c>docker</c> on <c>PATH</c>.
    /// </summary>
    public string DockerExecutable { get; set; } = "docker";

    /// <summary>
    /// Gets or sets the time limit in seconds for docker commands, from 1 to 600. It is the default for
    /// <c>docker_exec</c> and the limit for the other tools. Defaults to 60.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Checks the options.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="DockerHost"/> is set but lacks a supported scheme, or <see cref="DockerExecutable"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DefaultTimeoutSeconds"/> is outside 1-600.</exception>
    public void Validate()
    {
        if (DockerHost is not null && !_dockerHostSchemes.Any(scheme => DockerHost.StartsWith(scheme, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Docker host '{DockerHost}' must start with one of: {string.Join(", ", _dockerHostSchemes)}.",
                nameof(DockerHost));
        }

        OptionsValidation.RequireText(DockerExecutable, nameof(DockerExecutable));
        OptionsValidation.RequireInRange(DefaultTimeoutSeconds, 1, 600, nameof(DefaultTimeoutSeconds));
    }
}
