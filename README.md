# dotnet-mcp-toolkit

[![CI](https://github.com/Sarmkadan/dotnet-mcp-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/Sarmkadan/dotnet-mcp-toolkit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotnetMcpToolkit.svg)](https://www.nuget.org/packages/DotnetMcpToolkit)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Collection of ready-made [Model Context Protocol](https://modelcontextprotocol.io) servers for .NET: database, filesystem, git and docker tool packs that plug straight into Claude Desktop, Claude Code, GitHub Copilot or any other MCP client.

Compose the packs you need with a fluent builder, run over stdio, done.

## Features

- **Database pack** - schema introspection, read-only SQL queries, `EXPLAIN`. SQLite today; PostgreSQL, SQL Server and MySQL planned.
- **Filesystem pack** - list, read, write, search. Sandboxed to a root directory, optional read-only mode.
- **Git pack** - status, log, diff, blame for a working tree.
- **Docker pack** - `ps`, `logs`, `inspect`, `exec` against a local or remote daemon.
- **Custom tools** - implement `IMcpTool` and register it next to the built-ins.
- **Transports** - stdio (newline-delimited JSON-RPC 2.0); Streamable HTTP planned.
- Zero reflection magic, `net9.0`, nullable-clean, `TreatWarningsAsErrors`.

## Quick start

```csharp
using DotnetMcpToolkit;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddFileSystem(root: "/srv/repo", readOnly: true)
    .AddGit("/srv/repo")
    .AddDatabase("Host=localhost;Database=app", DatabaseProvider.PostgreSql)
    .AddDocker()
    .Build();

await using var transport = new StdioTransport(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput());

await server.RunAsync(transport);
```

Register the compiled binary in your MCP client, e.g. Claude Desktop `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "my-tools": { "command": "/path/to/MyTools" }
  }
}
```

## Tool packs

Each pack is added to the builder with one call, which registers its tools under fixed names. Text results come back as plain text; JSON results come back as one JSON document in a single text content block. Failures set `isError` and carry a message for the model.

### Filesystem

```csharp
using DotnetMcpToolkit;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddFileSystem(root: "/srv/repo", readOnly: true)
    .Build();
```

- `root` - directory the tools may access. Must exist. Relative paths resolve against it. Paths that escape it, directly or through a symbolic link, are rejected.
- `readOnly` - default `false`. When `true`, `fs_write_file` is not registered.

| Tool | Arguments | Returns |
|------|-----------|---------|
| `fs_list` | `path` string, default `"."`; `recursive` boolean, default `false`. At most 2000 entries. | JSON `{ entries: [{ path, type, size, modified }], truncated }`. `type` is `"file"` or `"dir"`; `size` (bytes) is present for files only; `modified` is UTC ISO 8601. `truncated` appears only when the 2000-entry limit is hit. Sorted by `path`. |
| `fs_read_file` | `path` string, required; `offset` integer, 1-based first line, default `1`; `limit` integer, 1-5000 lines, default `5000`. | Text: the selected lines joined with `\n`. Files over 10 MB need `offset` or `limit`. Binary files (a NUL byte in the first 8 KB) are rejected. |
| `fs_write_file` | `path` string, required, must name a file (no trailing `/` or `\`); `content` string, required, written as UTF-8 without BOM; `createDirectories` boolean, default `true`; `overwrite` boolean, default `true`. | JSON `{ path, bytesWritten }`, with `path` relative to the root. |
| `fs_search` | `pattern` string, required, a .NET regular expression of at most 1000 characters; `path` string, default `"."`; `glob` string, default `"*"`, a file name pattern without `/`, `\` or `..`; `maxResults` integer, 1-1000, default `200`; `ignoreCase` boolean, default `false`. | JSON `{ matches: [{ path, line, text }], truncated }`. `line` is 1-based. Binary files and files over 5 MB are skipped, symbolic links are not followed, and a regex that runs longer than 1 second fails. `truncated` is `true` when `maxResults` was reached. |

### Git

```csharp
using DotnetMcpToolkit;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddGit("/srv/repo")
    .Build();
```

- `repositoryPath` - path to a git working tree. Must exist. Git runs with it as the working directory, and arguments are passed as an argument list, never through a shell.

The git tools only read from the repository.

| Tool | Arguments | Returns |
|------|-----------|---------|
| `git_status` | None. | JSON `{ branch, ahead, behind, entries: [{ status, path }] }`. `branch` is `null` on a detached HEAD. `status` is the two-character porcelain code, such as `" M"` or `"??"`. |
| `git_log` | `maxCount` integer, 1-500, default `20`; `path` string, only commits that touch it; `since` string, any git date such as `"2 weeks ago"`; `author` string, pattern matched against the author. | JSON array `[{ sha, author, date, subject }]`, newest first. `date` is ISO 8601. |
| `git_diff` | `ref` string, revision to compare against, such as `HEAD~1` or a branch name; `staged` boolean, default `false`; `path` string; `contextLines` integer, 0-20, default `3`. Without `ref`, unstaged changes are compared with the index and staged changes with HEAD. | Text: unified diff. `ref` may contain only letters, digits and `_./~^@{}-`, and must not start with `-`. |
| `git_blame` | `path` string, required; `startLine` integer, 1-based, default `1`; `endLine` integer, 1-based and inclusive, default end of file. `startLine` must not exceed `endLine`. | JSON array `[{ line, sha, author, summary, text }]`, one entry per blamed line. |

### Database

```csharp
using DotnetMcpToolkit;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddDatabase("Data Source=app.db", DatabaseProvider.Sqlite, allowWrites: false)
    .Build();
```

- `connectionString` - ADO.NET connection string.
- `provider` - a `DatabaseProvider`. Only `Sqlite` ships in this package. `PostgreSql`, `SqlServer` and `MySql` are planned as separate `DotnetMcpToolkit.Database.*` packages; passing them throws `NotSupportedException` today.
- `allowWrites` - default `false`. When `false`, only single read statements run, and `INSERT`, `UPDATE`, `DELETE` and DDL are rejected.

To use a custom connection factory instead, pass an `IDbConnectionFactory`:

```csharp
using DotnetMcpToolkit;
using DotnetMcpToolkit.Database;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddDatabase(new SqliteConnectionFactory("Data Source=app.db"), allowWrites: false)
    .Build();
```

`AddDatabase(IDbConnectionFactory factory, bool allowWrites = false)` accepts any `IDbConnectionFactory`. Its `Provider` property tells the tools which catalog queries to use, and `OpenAsync` returns an open connection that the tool disposes.

| Tool | Arguments | Returns |
|------|-----------|---------|
| `sql_schema` | `table` string, optional: one table or view name, starting with a letter or `_`, then letters, digits, `_`, space, `.`, `-` or `$`, at most 128 characters. Omit it to describe everything. `includeSystem` boolean, default `false`. | JSON `{ tables: [{ name, type, columns: [{ name, type, nullable, primaryKey, defaultValue }], foreignKeys: [{ column, refTable, refColumn }], indexes: [{ name, unique, columns }] }] }`. `defaultValue` and `refColumn` are omitted when null. For factories other than SQLite, `foreignKeys` and `indexes` are always empty. |
| `sql_query` | `sql` string, required, a single statement of at most 100000 characters, using `@name` placeholders; `maxRows` integer, 1-5000, default `500`; `parameters` object whose values are strings, numbers, booleans or `null`, keyed by name (letters, digits and `_`, starting with a letter or `_`, at most 64 characters). | JSON `{ columns: [{ name, type }], rows, rowCount, truncated }`. `rows` is an array of arrays in column order; `truncated` is `true` when `maxRows` was reached. Statements that return no columns give `{ recordsAffected }` instead; writes run only when `allowWrites` is set. Times out after 30 seconds. |
| `sql_explain` | `sql` string, required, one read-only statement. The statement is not executed. | Text: plan lines joined with `\n`. On SQLite this is `EXPLAIN QUERY PLAN`. |

### Docker

```csharp
using DotnetMcpToolkit;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddDocker("unix:///var/run/docker.sock")
    .Build();
```

To enable `docker_exec`, pass `DockerOptions`:

```csharp
using DotnetMcpToolkit;
using DotnetMcpToolkit.Docker;

var server = new McpServerBuilder("my-tools", "1.0.0")
    .AddDocker(new DockerOptions(DockerHost: "unix:///var/run/docker.sock", AllowExec: true))
    .Build();
```

- `dockerHost` (`AddDocker(string? dockerHost = null)`) - daemon endpoint starting with `unix://`, `tcp://`, `npipe://` or `ssh://`; anything else throws `ArgumentException`. `null` uses `DOCKER_HOST` or the default socket.
- `DockerOptions.DockerHost` - daemon endpoint, as for `dockerHost`.
- `DockerOptions.AllowExec` - default `false`. When `true`, `docker_exec` is registered, because it runs commands inside containers.

The pack drives the `docker` CLI, so it must be installed and on `PATH`. Commands are limited to 60 seconds, except `docker_exec`, which uses its own `timeoutSeconds`.

| Tool | Arguments | Returns |
|------|-----------|---------|
| `docker_ps` | `all` boolean, default `false`, includes stopped containers. | JSON array `[{ id, names, image, status, state, ports }]`. |
| `docker_logs` | `container` string, required, name or id; `tail` integer, 1-5000, default `200`; `since` string passed to `docker logs --since`, such as `"10m"`; `timestamps` boolean, default `false`. | Text: stdout followed by stderr. |
| `docker_inspect` | `target` string, required, a name, id, `name:tag` or `repo@digest`, at most 256 characters; `type` string, one of `container`, `image`, `network` or `volume`, default `container`. | Text: the pretty-printed JSON array that `docker inspect` produces. |
| `docker_exec` | Only with `AllowExec`. `container` string, required; `command` array of non-empty strings, required, run without a shell; `workdir` absolute path inside the container; `user` string, such as `"app"` or `"1000:1000"`; `timeoutSeconds` integer, 1-600, default `60`. | JSON `{ exitCode, stdout, stderr }`. A non-zero exit code is a normal result. Exit codes 124 (timeout), 125, 126 and 127 are reported as errors. |

## Writing a custom tool

Implement `IMcpTool` and register it with `AddTool`. Tool names must be unique on the server, at most 64 characters, and free of whitespace; `Build` throws on duplicates. Over `StdioTransport` a tool must not write to stdout, because stdout carries the protocol. Log to stderr instead.

```csharp
using System.Globalization;
using System.Text.Json;
using DotnetMcpToolkit;

namespace TimeTools;

public static class Program
{
    public static async Task Main()
    {
        var server = new McpServerBuilder("time-tools", "1.0.0")
            .AddTool(new TimeTool())
            .Build();

        await using var transport = new StdioTransport(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput());

        await server.RunAsync(transport);
    }
}

/// <summary>
/// Returns the current UTC time as ISO 8601 text or as a JSON object.
/// </summary>
public sealed class TimeTool : IMcpTool
{
    private static readonly JsonElement _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "format": {
                    "type": "string",
                    "enum": ["text", "json"],
                    "description": "text returns the ISO 8601 time; json returns utc and unixSeconds (default json)"
                }
            }
        }
        """).RootElement.Clone();

    /// <inheritdoc />
    public string Name => "time_utc";

    /// <inheritdoc />
    public string Description => "Returns the current time in UTC.";

    /// <inheritdoc />
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc />
    public Task<McpToolResult> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken = default)
    {
        var format = "json";

        // arguments is null when the client sends no arguments object.
        if (arguments is { ValueKind: JsonValueKind.Object } args && args.TryGetProperty("format", out var formatProperty))
        {
            if (formatProperty.ValueKind != JsonValueKind.String)
            {
                return Task.FromResult(McpToolResult.Error("Argument 'format' must be a string"));
            }

            format = formatProperty.GetString() ?? format;
        }

        var now = DateTimeOffset.UtcNow;

        McpToolResult result = format switch
        {
            "text" => McpToolResult.Text(now.ToString("O", CultureInfo.InvariantCulture)),
            "json" => McpToolResult.Json(new { utc = now.ToString("O", CultureInfo.InvariantCulture), unixSeconds = now.ToUnixTimeSeconds() }),
            _ => McpToolResult.Error($"Unknown format '{format}'; use text or json"),
        };

        return Task.FromResult(result);
    }
}
```

Clients see `time_utc` with its schema. Calling it with `{ "format": "text" }` returns plain text; omitting the arguments returns the JSON object.

## Installation

```bash
dotnet add package DotnetMcpToolkit
```

## API overview

| Type | Purpose |
|------|---------|
| `McpServerBuilder` | Fluent composition: `AddFileSystem`, `AddGit`, `AddDatabase`, `AddDocker`, `AddTool`, `Build` |
| `IMcpServer` | Runs the JSON-RPC loop over a transport; exposes `Tools` |
| `IMcpTool` | One tool: `Name`, `Description`, `InputSchema`, `InvokeAsync` |
| `McpToolResult` / `McpContent` | Tool output (`Text`, `Error`, `Json` factories) |
| `IMcpTransport` / `StdioTransport` | Message transport abstraction and the stdio implementation |
| `DatabaseProvider` | Database engine selector; only `Sqlite` ships in this package |
| `IDbConnectionFactory` | Opens connections for the database pack; implement it to plug in a provider |
| `DockerOptions` | Docker pack configuration: daemon endpoint and the `AllowExec` opt-in |
| `PathSandbox` | Confines filesystem tool paths to a root directory |

## Status

Filesystem, git and docker packs and the SQLite database pack are implemented. PostgreSQL, SQL Server and MySQL providers are planned as separate packages, and Streamable HTTP is planned. Version 0.1.0; the public API may still change before 1.0. See [CLAUDE.md](CLAUDE.md) for architecture notes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) - Copyright 2026 Vladyslav Zaiets
