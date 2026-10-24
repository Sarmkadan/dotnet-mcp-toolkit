using System.Text.RegularExpressions;

namespace DotnetMcpToolkit.Docker;

/// <summary>
/// Argument patterns shared by the docker tools. Anchors use <c>\z</c> so a trailing newline cannot slip through.
/// </summary>
internal static partial class DockerPatterns
{
    /// <summary>
    /// Matches a container name or id: letters, digits, <c>_</c>, <c>.</c>, <c>-</c>, 1-128 characters.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z")]
    internal static partial Regex ContainerName();

    /// <summary>
    /// Matches an object reference for <c>docker inspect</c>: a name, id, <c>name:tag</c> or <c>repo@digest</c>, 1-256 characters.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.:/@-]{0,255}\z")]
    internal static partial Regex InspectTarget();

    /// <summary>
    /// Matches a user for <c>docker exec -u</c>: a name, a uid, or <c>user:group</c>, 1-128 characters.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.:-]{0,127}\z")]
    internal static partial Regex UserName();
}
