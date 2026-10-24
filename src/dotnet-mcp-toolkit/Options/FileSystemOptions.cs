namespace DotnetMcpToolkit.Options;

/// <summary>
/// Configuration for the filesystem tool pack. Binds from a configuration section such as <c>Mcp:FileSystem</c>.
/// </summary>
public sealed class FileSystemOptions
{
    /// <summary>
    /// Gets or sets the directory the tools may access; paths outside it are rejected. Required, and must exist.
    /// </summary>
    public string Root { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether <c>fs_write_file</c> is left out. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries <c>fs_list</c> returns. Must be at least 1. Defaults to 2000.
    /// </summary>
    public int MaxListEntries { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the largest file <c>fs_read_file</c> reads without an offset and limit, in bytes. Must be at least 1. Defaults to 10 MB.
    /// </summary>
    public long MaxReadFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of matching lines <c>fs_search</c> returns. Must be at least 1. Defaults to 1000.
    /// </summary>
    public int MaxSearchResults { get; set; } = 1000;

    /// <summary>
    /// Checks the options.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="Root"/> is null, empty or whitespace, or does not exist as a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is below its minimum.</exception>
    public void Validate()
    {
        OptionsValidation.RequireText(Root, nameof(Root));

        if (!Directory.Exists(Path.GetFullPath(Root)))
        {
            throw new ArgumentException($"Root directory does not exist: {Root}", nameof(Root));
        }

        OptionsValidation.RequireAtLeast(MaxListEntries, 1, nameof(MaxListEntries));
        OptionsValidation.RequireAtLeast(MaxReadFileBytes, 1, nameof(MaxReadFileBytes));
        OptionsValidation.RequireAtLeast(MaxSearchResults, 1, nameof(MaxSearchResults));
    }
}
