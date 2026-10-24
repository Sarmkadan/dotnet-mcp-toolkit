using System.Runtime.InteropServices;

namespace DotnetMcpToolkit.Tests.Docker;

/// <summary>
/// Writes a POSIX shell script that stands in for the <c>docker</c> CLI, so tests never touch a real daemon.
/// The directory is removed on <see cref="Dispose"/>.
/// </summary>
public sealed class FakeDockerExecutable : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fakedocker-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Creates the fake executable with the given shell <paramref name="body"/>.
    /// </summary>
    /// <param name="body">Shell statements executed for every invocation; <c>$@</c> holds the docker arguments.</param>
    public FakeDockerExecutable(string body)
    {
        Directory.CreateDirectory(_directory);

        Path = System.IO.Path.Combine(_directory, "docker");
        File.WriteAllText(Path, "#!/bin/sh\n" + body + "\n");

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Gets the full path of the script.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the path where a script can write the arguments it received, i.e. <c>$(dirname "$0")/args.txt</c>.
    /// </summary>
    public string ArgumentsPath => System.IO.Path.Combine(_directory, "args.txt");

    /// <summary>
    /// Gets a value indicating whether the shell-script stand-in can run on this platform.
    /// </summary>
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
