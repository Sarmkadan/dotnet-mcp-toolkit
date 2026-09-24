# dotnet-mcp-toolkit

Ready-made MCP (Model Context Protocol) servers for .NET: database, filesystem, git, docker tool packs plus a fluent builder and stdio transport. Library package `DotnetMcpToolkit`, target `net9.0`.

## Build / test

```bash
dotnet build                      # whole solution, warnings are errors
dotnet test                       # xunit, tests/dotnet-mcp-toolkit.Tests
dotnet test --filter "FullyQualifiedName~PlaceholderTests"
dotnet pack src/dotnet-mcp-toolkit -c Release -o artifacts
dotnet format --verify-no-changes # style check, enforced in CI via EnforceCodeStyleInBuild
```

## Key directories

- `src/dotnet-mcp-toolkit/` - the library (`DotnetMcpToolkit` namespace). One public type per file.
- `tests/dotnet-mcp-toolkit.Tests/` - xunit tests. Mirror the source file layout: `Foo.cs` -> `FooTests.cs`.
- `.github/workflows/ci.yml` - build + test on push/PR to `main`.
- `Directory.Build.props` - shared compiler settings (Nullable, LangVersion latest, TreatWarningsAsErrors, analyzers).

## Architecture

```
McpServerBuilder  --Build()-->  IMcpServer  --RunAsync(IMcpTransport)-->  JSON-RPC loop
      |                              |
      | AddFileSystem/AddGit/        | Tools : IReadOnlyList<IMcpTool>
      | AddDatabase/AddDocker/       |
      | AddTool                      v
      +------------------------> IMcpTool.InvokeAsync(JsonElement? args) -> McpToolResult
```

- `IMcpTransport` moves raw JSON strings; it knows nothing about MCP. `StdioTransport` is newline-delimited JSON over stdin/stdout. Nothing else may write to stdout while a stdio server runs (log to stderr).
- `IMcpServer` owns the protocol: `initialize`, `tools/list`, `tools/call`, `ping`, notifications. Protocol errors -> JSON-RPC error objects; tool domain errors -> `McpToolResult.IsError = true`.
- Tool packs live in their own folders (`FileSystem/`, `Git/`, `Database/`, `Docker/`) once implemented. Each pack is a set of `IMcpTool` classes plus a registration extension on `McpServerBuilder`.
- Database/Docker providers should be pluggable per pack; provider-specific NuGet dependencies must not leak into the core library (plan: separate `DotnetMcpToolkit.Database.*` packages later).

## Conventions

- Current state: interfaces and public signatures exist, bodies throw `NotImplementedException`. Implement without changing public signatures unless there is a reason; document the reason in the PR.
- `System.Text.Json` only, source-generated contexts where the type set is closed. No Newtonsoft.
- Async all the way: `Task`/`ValueTask`, always accept a `CancellationToken`, no `.Result`/`.Wait()`.
- Security defaults: filesystem sandboxed to root (reject `..` and symlink escapes), database read-only unless `allowWrites`, docker `exec` opt-in.
- XML docs on every public member (`GenerateDocumentationFile` is on, missing docs are build errors).
- Tests: Arrange/Act/Assert, one behaviour per test, names `Method_Scenario_Expectation`.
- Commits: conventional (`feat:`, `fix:`, `test:`, `docs:`, `chore:`). No `Co-Authored-By` trailers.
