using System.Text;

namespace DotnetMcpToolkit.Tests.FileSystem;

/// <summary>
/// Builds a temporary tree shared by the read-only FileSystem tests and deletes it on dispose:
/// <c>readme.md</c> (3 lines), <c>src/Program.cs</c> (10 lines, the hello call on line 4),
/// <c>src/bin/app.dll</c> (binary, contains NUL bytes), <c>docs/notes.txt</c> (50 lines).
/// </summary>
public sealed class TempTreeFixture : IDisposable
{
    /// <summary>The content of <c>readme.md</c>, one entry per line.</summary>
    public static readonly string[] ReadmeLines = ["# Sample", "A readme.", "Last line."];

    /// <summary>The content of line 4 of <c>src/Program.cs</c>.</summary>
    public const string HelloLine = "Console.WriteLine(\"hello\");";

    private static readonly string[] _programLines =
    [
        "// Sample entry point",
        "using System;",
        "",
        HelloLine,
        "",
        "static class Helpers",
        "{",
        "    public static int Answer() => 42;",
        "}",
        "// end",
    ];

    /// <summary>Creates the tree under a new temporary directory.</summary>
    public TempTreeFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "fs-tree-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(Root, "src", "bin"));
        Directory.CreateDirectory(Path.Combine(Root, "docs"));

        File.WriteAllText(Path.Combine(Root, "readme.md"), string.Join('\n', ReadmeLines) + '\n');
        File.WriteAllText(Path.Combine(Root, "src", "Program.cs"), string.Join('\n', _programLines) + '\n');

        // The binary payload contains the text "Console.WriteLine" so that a search which did not skip it would match.
        File.WriteAllBytes(
            Path.Combine(Root, "src", "bin", "app.dll"),
            [0x4D, 0x5A, 0x00, 0x00, .. Encoding.ASCII.GetBytes("Console.WriteLine")]);

        File.WriteAllText(
            Path.Combine(Root, "docs", "notes.txt"),
            string.Join('\n', Enumerable.Range(1, 50).Select(i => $"note {i}")) + '\n');
    }

    /// <summary>The absolute path of the tree root.</summary>
    public string Root { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
