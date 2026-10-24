using System.Text.Json;

namespace DotnetMcpToolkit;

/// <summary>
/// Validates tool arguments supplied by the model. Every helper returns an error result that names the
/// offending argument, or <see langword="null"/> when the argument is acceptable. A JSON <c>null</c> value
/// and a missing property are treated the same way: the argument was not provided.
/// </summary>
internal static class ToolArgumentValidation
{
    /// <summary>
    /// Checks that the arguments object, when provided, is a JSON object.
    /// </summary>
    /// <param name="args">The arguments as received by the tool, or <see langword="null"/>.</param>
    /// <returns>An error result when the arguments are not an object; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? RequireObjectArguments(JsonElement? args)
    {
        if (args is null || args.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return McpToolResult.Error("Tool arguments must be a JSON object");
    }

    /// <summary>
    /// Reads a required, non-empty string argument.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">The string value when the method returns <see langword="null"/>; otherwise an empty string.</param>
    /// <param name="allowEmpty">Whether an empty string is accepted.</param>
    /// <returns>An error result when the argument is missing, not a string, or empty; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? RequireString(JsonElement? args, string name, out string value, bool allowEmpty = false)
    {
        value = string.Empty;

        if (TryGetProperty(args, name, out var prop) &&
            prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { } text &&
            (allowEmpty || text.Length > 0))
        {
            value = text;
            return null;
        }

        return McpToolResult.Error(allowEmpty
            ? $"Argument '{name}' is required and must be a string"
            : $"Argument '{name}' is required and must be a non-empty string");
    }

    /// <summary>
    /// Reads a required path argument. Besides the checks of <see cref="RequireString"/>, the path must not contain NUL characters.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">The path when the method returns <see langword="null"/>; otherwise an empty string.</param>
    /// <returns>An error result when the argument is invalid; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? RequirePath(JsonElement? args, string name, out string value)
    {
        var error = RequireString(args, name, out value);

        return error ?? RejectNulCharacters(value, name);
    }

    /// <summary>
    /// Reads an optional string argument.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value used when the argument is absent or <see langword="null"/>.</param>
    /// <param name="value">The string value when the method returns <see langword="null"/>.</param>
    /// <returns>An error result when the argument is not a string; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? OptionalString(JsonElement? args, string name, string defaultValue, out string value)
    {
        value = defaultValue;

        if (!TryGetProperty(args, name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? defaultValue;
            return null;
        }

        return McpToolResult.Error($"Argument '{name}' must be a string");
    }

    /// <summary>
    /// Reads an optional path argument. Besides the checks of <see cref="OptionalString"/>, the path must not contain NUL characters.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value used when the argument is absent or <see langword="null"/>.</param>
    /// <param name="value">The path when the method returns <see langword="null"/>.</param>
    /// <returns>An error result when the argument is invalid; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? OptionalPath(JsonElement? args, string name, string defaultValue, out string value)
    {
        var error = OptionalString(args, name, defaultValue, out value);

        return error ?? RejectNulCharacters(value, name);
    }

    /// <summary>
    /// Reads an optional integer argument within an inclusive range.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="min">Smallest accepted value.</param>
    /// <param name="max">Largest accepted value.</param>
    /// <param name="defaultValue">Value used when the argument is absent or <see langword="null"/>.</param>
    /// <param name="value">The integer value when the method returns <see langword="null"/>.</param>
    /// <returns>An error result when the argument is not an integer or is out of range; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? OptionalInt32(JsonElement? args, string name, int min, int max, int defaultValue, out int value)
    {
        value = defaultValue;

        if (!TryGetProperty(args, name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var number) && number >= min && number <= max)
        {
            value = number;
            return null;
        }

        return McpToolResult.Error($"Argument '{name}' must be an integer between {min} and {max}");
    }

    /// <summary>
    /// Reads an optional boolean argument.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <param name="defaultValue">Value used when the argument is absent or <see langword="null"/>.</param>
    /// <param name="value">The boolean value when the method returns <see langword="null"/>.</param>
    /// <returns>An error result when the argument is not a boolean; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? OptionalBoolean(JsonElement? args, string name, bool defaultValue, out bool value)
    {
        value = defaultValue;

        if (!TryGetProperty(args, name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return null;
        }

        return McpToolResult.Error($"Argument '{name}' must be a boolean");
    }

    /// <summary>
    /// Checks that a string argument does not exceed <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="value">The validated string value.</param>
    /// <param name="name">Property name.</param>
    /// <param name="maxLength">Largest accepted length in characters.</param>
    /// <returns>An error result when the value is too long; otherwise <see langword="null"/>.</returns>
    internal static McpToolResult? RequireMaxLength(string value, string name, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return null;
        }

        return McpToolResult.Error($"Argument '{name}' must be at most {maxLength} characters");
    }

    /// <summary>
    /// Reports whether the argument is present with a value other than <see langword="null"/>.
    /// </summary>
    /// <param name="args">The arguments object, or <see langword="null"/>.</param>
    /// <param name="name">Property name.</param>
    /// <returns><see langword="true"/> when the argument was provided; otherwise <see langword="false"/>.</returns>
    internal static bool HasArgument(JsonElement? args, string name) =>
        TryGetProperty(args, name, out var prop) && prop.ValueKind != JsonValueKind.Null;

    private static McpToolResult? RejectNulCharacters(string path, string name)
    {
        if (path.Contains('\0'))
        {
            return McpToolResult.Error($"{name} contains invalid characters");
        }

        return null;
    }

    /// <summary>
    /// Looks up a property. Non-object arguments never have properties, so the lookup is skipped for them
    /// instead of throwing <see cref="InvalidOperationException"/>.
    /// </summary>
    private static bool TryGetProperty(JsonElement? args, string name, out JsonElement value)
    {
        if (args is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
