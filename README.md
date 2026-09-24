# dotnet-mcp-toolkit

[![CI](https://github.com/Sarmkadan/dotnet-mcp-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/Sarmkadan/dotnet-mcp-toolkit/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotnetMcpToolkit.svg)](https://www.nuget.org/packages/DotnetMcpToolkit)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Collection of ready-made [Model Context Protocol](https://modelcontextprotocol.io) servers for .NET: database, filesystem, git and docker tool packs that plug straight into Claude Desktop, Claude Code, GitHub Copilot or any other MCP client.

Compose the packs you need with a fluent builder, run over stdio, done.

## Features

- **Database pack** - schema introspection, read-only SQL queries, `EXPLAIN`. SQLite, PostgreSQL, SQL Server, MySQL.
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
| `DatabaseProvider` | Supported database engines |

## Status

Early scaffold: public API shape is defined, implementations are in progress. See [CLAUDE.md](CLAUDE.md) for architecture notes.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) - Copyright 2026 Vladyslav Zaiets
