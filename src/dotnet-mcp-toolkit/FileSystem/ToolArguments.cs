using System.Text.Json;

namespace DotnetMcpToolkit.FileSystem;

/// <summary>
/// Helpers for reading typed values from a <see cref="JsonElement"/> arguments object.
/// </summary>
internal static class ToolArguments
{
    /// <summary>
    /// Gets a string property from <paramref name="args"/>, returning <paramref name="defaultValue"/>
    /// when the property is missing or <paramref name="args"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value to return when the property is absent.</param>
    /// <returns>The string value, or <paramref name="defaultValue"/>.</returns>
    internal static string? GetString(JsonElement? args, string name, string? defaultValue = null)
    {
        if (args is not { } element)
        {
            return defaultValue;
        }

        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? defaultValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets an integer property from <paramref name="args"/>, returning <paramref name="defaultValue"/>
    /// when the property is missing or <paramref name="args"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value to return when the property is absent.</param>
    /// <returns>The integer value, or <paramref name="defaultValue"/>.</returns>
    internal static int? GetInt32(JsonElement? args, string name, int? defaultValue = null)
    {
        if (args is not { } element)
        {
            return defaultValue;
        }

        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets a boolean property from <paramref name="args"/>, returning <paramref name="defaultValue"/>
    /// when the property is missing or <paramref name="args"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value to return when the property is absent.</param>
    /// <returns>The boolean value, or <paramref name="defaultValue"/>.</returns>
    internal static bool GetBoolean(JsonElement? args, string name, bool defaultValue = false)
    {
        if (args is not { } element)
        {
            return defaultValue;
        }

        if (element.TryGetProperty(name, out var prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
        {
            return prop.GetBoolean();
        }

        return defaultValue;
    }
}
