using DotnetMcpToolkit.Database;
using DotnetMcpToolkit.Docker;
using DotnetMcpToolkit.FileSystem;
using DotnetMcpToolkit.Git;
using DotnetMcpToolkit.Options;

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
    private const int _maxToolNameLength = 64;

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
    /// Creates a builder with the server identity and every tool pack set in <paramref name="options"/>.
    /// A pack is registered only when its options are not <see langword="null"/>. Add more tools to the returned
    /// builder before <see cref="Build"/> if needed.
    /// </summary>
    /// <param name="options">Root configuration, typically bound from the <c>Mcp</c> configuration section.</param>
    /// <returns>A builder holding the tools of every configured pack.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A value failed validation; see <see cref="McpServerOptions.Validate"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is outside its range; see <see cref="McpServerOptions.Validate"/>.</exception>
    /// <exception cref="NotSupportedException">The database provider is not shipped in this package.</exception>
    public static McpServerBuilder FromOptions(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var builder = new McpServerBuilder(options.Name, options.Version);

        if (options.FileSystem is { } fileSystem)
        {
            builder.AddFileSystem(fileSystem);
        }

        if (options.Git is { } git)
        {
            builder.AddGit(git);
        }

        if (options.Database is { } database)
        {
            builder.AddDatabase(database);
        }

        if (options.Docker is { } docker)
        {
            builder.AddDocker(docker);
        }

        return builder;
    }

    /// <summary>
    /// Registers a custom tool.
    /// </summary>
    /// <param name="tool">The tool to expose.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The tool's <see cref="IMcpTool.Name"/> is null or whitespace, contains whitespace characters, or is longer than 64 characters.
    /// </exception>
    public McpServerBuilder AddTool(IMcpTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ValidateToolName(tool.Name);

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
    /// <exception cref="ArgumentException"><paramref name="root"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="root"/> does not exist.</exception>
    public McpServerBuilder AddFileSystem(string root, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        return RegisterFileSystem(new PathSandbox(root), new FileSystemOptions { ReadOnly = readOnly });
    }

    /// <summary>
    /// Adds the filesystem tool pack configured by <paramref name="options"/>, including its size and result limits.
    /// </summary>
    /// <param name="options">Root directory, read-only flag and limits.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="FileSystemOptions.Root"/> is empty, whitespace or does not exist as a directory.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is below its minimum.</exception>
    public McpServerBuilder AddFileSystem(FileSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return RegisterFileSystem(new PathSandbox(options.Root), options);
    }

    /// <summary>
    /// Adds the git tool pack (<c>git_status</c>, <c>git_log</c>, <c>git_diff</c>, <c>git_blame</c>) for a repository.
    /// </summary>
    /// <param name="repositoryPath">Path to the working tree.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="repositoryPath"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="repositoryPath"/> does not exist.</exception>
    public McpServerBuilder AddGit(string repositoryPath)
    {
        return AddGit(new GitOptions { RepositoryPath = repositoryPath });
    }

    /// <summary>
    /// Adds the git tool pack configured by <paramref name="options"/>: repository, git executable and the <c>git_log</c> limit.
    /// </summary>
    /// <param name="options">Repository path, git executable and limits.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="GitOptions.RepositoryPath"/> or <see cref="GitOptions.GitExecutable"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="GitOptions.MaxLogCount"/> is outside 1-500.</exception>
    /// <exception cref="DirectoryNotFoundException"><see cref="GitOptions.RepositoryPath"/> does not exist.</exception>
    public McpServerBuilder AddGit(GitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var runner = new GitCommandRunner(options.RepositoryPath, options.GitExecutable);

        AddTool(new GitStatusTool(runner));
        AddTool(new GitLogTool(runner, options.MaxLogCount));
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
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="provider"/> is not a defined <see cref="DatabaseProvider"/> value.</exception>
    /// <exception cref="NotSupportedException"><paramref name="provider"/> is not shipped in this package.</exception>
    public McpServerBuilder AddDatabase(string connectionString, DatabaseProvider provider, bool allowWrites = false)
    {
        return AddDatabase(new DatabaseOptions
        {
            ConnectionString = connectionString,
            Provider = provider,
            AllowWrites = allowWrites,
        });
    }

    /// <summary>
    /// Adds the database tool pack for a built-in provider, configured by <paramref name="options"/>:
    /// connection, write permission, row limit and command timeout.
    /// </summary>
    /// <param name="options">Connection, provider and limits.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="DatabaseOptions.ConnectionString"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="DatabaseOptions.Provider"/> is not defined, <see cref="DatabaseOptions.MaxRows"/> is outside 1-5000, or
    /// <see cref="DatabaseOptions.CommandTimeoutSeconds"/> is outside 1-600.
    /// </exception>
    /// <exception cref="NotSupportedException"><see cref="DatabaseOptions.Provider"/> is not shipped in this package.</exception>
    public McpServerBuilder AddDatabase(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return options.Provider switch
        {
            DatabaseProvider.Sqlite => RegisterDatabase(new SqliteConnectionFactory(options.ConnectionString), options),
            _ => throw new NotSupportedException($"Provider {options.Provider} requires the DotnetMcpToolkit.Database.{options.Provider} package"),
        };
    }

    /// <summary>
    /// Adds the database tool pack (<c>sql_query</c>, <c>sql_schema</c>, <c>sql_explain</c>) over a custom connection factory.
    /// </summary>
    /// <param name="factory">Source of database connections.</param>
    /// <param name="allowWrites">Whether <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>/DDL statements are permitted.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public McpServerBuilder AddDatabase(IDbConnectionFactory factory, bool allowWrites = false)
    {
        return RegisterDatabase(factory, new DatabaseOptions { AllowWrites = allowWrites });
    }

    /// <summary>
    /// Adds the read-only docker tool pack (<c>docker_ps</c>, <c>docker_logs</c>, <c>docker_inspect</c>).
    /// <c>docker_exec</c> is not registered; use <see cref="AddDocker(DockerOptions)"/> with <see cref="DockerOptions.AllowExec"/> to opt in.
    /// </summary>
    /// <param name="dockerHost">
    /// Docker daemon endpoint starting with <c>unix://</c>, <c>tcp://</c>, <c>npipe://</c> or <c>ssh://</c>;
    /// <see langword="null"/> uses <c>DOCKER_HOST</c> or the default socket.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="dockerHost"/> is empty, whitespace or lacks a supported scheme.</exception>
    public McpServerBuilder AddDocker(string? dockerHost = null)
    {
        return AddDocker(new DockerOptions { DockerHost = dockerHost });
    }

    /// <summary>
    /// Adds the docker tool pack (<c>docker_ps</c>, <c>docker_logs</c>, <c>docker_inspect</c>), and <c>docker_exec</c> when
    /// <see cref="DockerOptions.AllowExec"/> is set.
    /// </summary>
    /// <param name="options">Daemon endpoint, docker executable, exec opt-in and default timeout.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="DockerOptions.DockerHost"/> lacks a supported scheme, or <see cref="DockerOptions.DockerExecutable"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DockerOptions.DefaultTimeoutSeconds"/> is outside 1-600.</exception>
    public McpServerBuilder AddDocker(DockerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var runner = new DockerCliRunner(
            options.DockerHost,
            options.DockerExecutable,
            TimeSpan.FromSeconds(options.DefaultTimeoutSeconds));

        AddTool(new DockerPsTool(runner));
        AddTool(new DockerLogsTool(runner));
        AddTool(new DockerInspectTool(runner));

        if (options.AllowExec)
        {
            AddTool(new DockerExecTool(runner, options.DefaultTimeoutSeconds));
        }

        return this;
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

    private McpServerBuilder RegisterFileSystem(PathSandbox sandbox, FileSystemOptions options)
    {
        AddTool(new FsListTool(sandbox, options.MaxListEntries));
        AddTool(new FsReadFileTool(sandbox, options.MaxReadFileBytes));
        AddTool(new FsSearchTool(sandbox, options.MaxSearchResults));

        if (!options.ReadOnly)
        {
            AddTool(new FsWriteFileTool(sandbox));
        }

        return this;
    }

    private McpServerBuilder RegisterDatabase(IDbConnectionFactory factory, DatabaseOptions options)
    {
        var commandTimeout = TimeSpan.FromSeconds(options.CommandTimeoutSeconds);

        AddTool(new SqlQueryTool(factory, options.AllowWrites, options.MaxRows, commandTimeout));
        AddTool(new SqlSchemaTool(factory));
        AddTool(new SqlExplainTool(factory, commandTimeout));

        return this;
    }

    private static void ValidateToolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"Tool name '{name}' must not be null or whitespace.", nameof(name));
        }

        if (name.Length > _maxToolNameLength)
        {
            throw new ArgumentException(
                $"Tool name '{name}' is {name.Length} characters long; the maximum is {_maxToolNameLength}.",
                nameof(name));
        }

        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Tool name '{name}' must not contain whitespace.", nameof(name));
        }
    }
}
