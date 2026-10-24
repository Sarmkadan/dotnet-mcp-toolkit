namespace DotnetMcpToolkit.Options;

/// <summary>
/// Shared checks for the options classes. Every failure names the offending property.
/// </summary>
internal static class OptionsValidation
{
    /// <summary>
    /// Requires a value that is neither null, empty nor whitespace.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="propertyName">Name of the property, used as the parameter name of the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    internal static void RequireText(string? value, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, propertyName);
    }

    /// <summary>
    /// Requires an integer within an inclusive range.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">Smallest accepted value.</param>
    /// <param name="max">Largest accepted value.</param>
    /// <param name="propertyName">Name of the property, used as the parameter name of the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is outside <paramref name="min"/>..<paramref name="max"/>.</exception>
    internal static void RequireInRange(int value, int min, int max, string propertyName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} must be between {min} and {max}.");
        }
    }

    /// <summary>
    /// Requires a number of at least <paramref name="min"/>.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">Smallest accepted value.</param>
    /// <param name="propertyName">Name of the property, used as the parameter name of the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than <paramref name="min"/>.</exception>
    internal static void RequireAtLeast(long value, long min, string propertyName)
    {
        if (value < min)
        {
            throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} must be at least {min}.");
        }
    }

    /// <summary>
    /// Requires a value that is one of the defined members of an enum.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="propertyName">Name of the property, used as the parameter name of the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a defined member of <typeparamref name="TEnum"/>.</exception>
    internal static void RequireDefined<TEnum>(TEnum value, string propertyName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(propertyName, value, $"{propertyName} is not a defined value.");
        }
    }
}
