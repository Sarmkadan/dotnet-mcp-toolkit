namespace DotnetMcpToolkit.Options;

/// <summary>
/// Root configuration for a server built by <see cref="McpServerBuilder.FromOptions"/>. Binds from a configuration
/// section such as <c>Mcp</c>. A pack is registered only when its section is set.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Gets or sets the server name reported to the client. Required.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the server version reported to the client. Required. Defaults to <c>1.0.0</c>.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the filesystem pack configuration, or <see langword="null"/> to leave the pack out.
    /// </summary>
    public FileSystemOptions? FileSystem { get; set; }

    /// <summary>
    /// Gets or sets the git pack configuration, or <see langword="null"/> to leave the pack out.
    /// </summary>
    public GitOptions? Git { get; set; }

    /// <summary>
    /// Gets or sets the database pack configuration, or <see langword="null"/> to leave the pack out.
    /// </summary>
    public DatabaseOptions? Database { get; set; }

    /// <summary>
    /// Gets or sets the docker pack configuration, or <see langword="null"/> to leave the pack out.
    /// </summary>
    public DockerOptions? Docker { get; set; }

    /// <summary>
    /// Checks the server identity and every pack configuration that is set.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="Name"/> or <see cref="Version"/> is null, empty or whitespace, or a pack check fails.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A pack limit is outside its range.</exception>
    public void Validate()
    {
        OptionsValidation.RequireText(Name, nameof(Name));
        OptionsValidation.RequireText(Version, nameof(Version));

        FileSystem?.Validate();
        Git?.Validate();
        Database?.Validate();
        Docker?.Validate();
    }
}
