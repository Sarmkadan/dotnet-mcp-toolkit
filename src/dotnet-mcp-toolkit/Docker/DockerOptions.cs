namespace DotnetMcpToolkit.Docker;

/// <summary>
/// Configuration for the docker tool pack.
/// </summary>
/// <param name="DockerHost">
/// Daemon endpoint passed as <c>--host</c>, e.g. <c>unix:///var/run/docker.sock</c>.
/// <see langword="null"/> or empty uses the CLI default (<c>DOCKER_HOST</c> or the local socket).
/// </param>
/// <param name="AllowExec">
/// When <see langword="true"/>, <c>docker_exec</c> is registered. It runs commands inside containers, so it is off by default.
/// </param>
public sealed record DockerOptions(string? DockerHost = null, bool AllowExec = false);
