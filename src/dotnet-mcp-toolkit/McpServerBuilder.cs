using DotnetMcpToolkit.Database;
using DotnetMcpToolkit.FileSystem;
using DotnetMcpToolkit.Git;

namespace DotnetMcpToolkit;

/// <summary>
/// Fluent entry point for assembling an <see cref="IMcpServer"/> from the built-in tool packs
/// (database, filesystem, git, docker) and custom tools.
/// </summary>
/// <example>
/// <code>
/// var server = new McpServerBuilder("my-tools", "1.0.0")
///     .AddFileSystem(root: "/srv/repo", readOnly: true)
///     .AddGit("/srv/repo")
///     .AddDatabase("Host=localhost;Database=app", DatabaseProvider.PostgreSql)
///     .AddDocker()
///     .Build();
///
/// await using var transport = new StdioTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
/// await server.RunAsync(transport);
/// </code>
/// </example>
public sealed class McpServerBuilder
{
    private readonly string _name;
    private readonly string _version;
    private readonly List<IMcpTool> _tools = [];

    /// <summary>
    /// Creates a builder for a server with the given identity.
    /// </summary>
    /// <param name="name">Server name reported to the client.</param>
    /// <param name="version">Server version reported to the client.</param>
    public McpServerBuilder(string name, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        _name = name;
        _version = version;
    }

    /// <summary>
    /// Gets the tools registered so far, in registration order. Use it to inspect the builder before <see cref="Build"/>.
    /// </summary>
    public IReadOnlyList<IMcpTool> Tools => _tools;

    /// <summary>
    /// Registers a custom tool.
    /// </summary>
    /// <param name="tool">The tool to expose.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The tool's <see cref="IMcpTool.Name"/> is null or whitespace.</exception>
    public McpServerBuilder AddTool(IMcpTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);

        _tools.Add(tool);
        return this;
    }

    /// <summary>
    /// Adds the filesystem tool pack (<c>fs_list</c>, <c>fs_read_file</c>, <c>fs_write_file</c>, <c>fs_search</c>),
    /// sandboxed to <paramref name="root"/>.
    /// </summary>
    /// <param name="root">Directory the tools may access; paths outside it are rejected.</param>
    /// <param name="readOnly">When <see langword="true"/>, write tools are not registered.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> does not exist.</exception>
    public McpServerBuilder AddFileSystem(string root, bool readOnly = false)
    {
        var sandbox = new PathSandbox(root);

        AddTool(new FsListTool(sandbox));
        AddTool(new FsReadFileTool(sandbox));
        AddTool(new FsSearchTool(sandbox));

        if (!readOnly)
        {
            AddTool(new FsWriteFileTool(sandbox));
        }

        return this;
    }

    /// <summary>
    /// Adds the git tool pack (<c>git_status</c>, <c>git_log</c>, <c>git_diff</c>, <c>git_blame</c>) for a repository.
    /// </summary>
    /// <param name="repositoryPath">Path to the working tree.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> is empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="repositoryPath"/> does not exist.</exception>
    public McpServerBuilder AddGit(string repositoryPath)
    {
        var runner = new GitCommandRunner(repositoryPath);

        AddTool(new GitStatusTool(runner));
        AddTool(new GitLogTool(runner));
        AddTool(new GitDiffTool(runner));
        AddTool(new GitBlameTool(runner));

        return this;
    }

    /// <summary>
    /// Adds the database tool pack for a built-in provider. Queries are read-only by default.
    /// Only <see cref="DatabaseProvider.Sqlite"/> ships in this package; other providers come from their own packages
    /// and are added through <see cref="AddDatabase(IDbConnectionFactory, bool)"/>.
    /// </summary>
    /// <param name="connectionString">ADO.NET connection string.</param>
    /// <param name="provider">Database engine.</param>
    /// <param name="allowWrites">Whether <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>/DDL statements are permitted.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or whitespace.</exception>
    /// <exception cref="NotSupportedException"><paramref name="provider"/> is not shipped in this package.</exception>
    public McpServerBuilder AddDatabase(string connectionString, DatabaseProvider provider, bool allowWrites = false)
    {
        return provider switch
        {
            DatabaseProvider.Sqlite => AddDatabase(new SqliteConnectionFactory(connectionString), allowWrites),
            _ => throw new NotSupportedException($"Provider {provider} requires the DotnetMcpToolkit.Database.{provider} package"),
        };
    }

    /// <summary>
    /// Adds the database tool pack (<c>sql_query</c>) over a custom connection factory.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <param name="allowWrites">Whether <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>/DDL statements are permitted.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public McpServerBuilder AddDatabase(IDbConnectionFactory factory, bool allowWrites = false)
    {
        AddTool(new SqlQueryTool(factory, allowWrites));

        return this;
    }

    /// <summary>
    /// Adds the docker tool pack (<c>docker_ps</c>, <c>docker_logs</c>, <c>docker_inspect</c>, <c>docker_exec</c>).
    /// </summary>
    /// <param name="dockerHost">Docker daemon endpoint; <see langword="null"/> uses <c>DOCKER_HOST</c> or the default socket.</param>
    public McpServerBuilder AddDocker(string? dockerHost = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Builds the server. Throws if two registered tools share a name.
    /// </summary>
    /// <returns>A server exposing a snapshot of the tools registered so far.</returns>
    /// <exception cref="InvalidOperationException">Two registered tools share the same name.</exception>
    public IMcpServer Build()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tool in _tools)
        {
            if (!names.Add(tool.Name))
            {
                throw new InvalidOperationException($"Duplicate tool name '{tool.Name}'");
            }
        }

        return new McpServer(_name, _version, _tools.ToArray());
    }
}
