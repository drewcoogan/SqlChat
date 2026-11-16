using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SqlChat.Domain.Extensions;

/// <summary>Argument guards used inside the SqlChat packages. Not intended for consumer code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ObjectExtensions
{
    public static T Validate<T>(this T value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return value;
    }

    public static T Validate<T>(this T value, Func<T, bool> validator, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (validator(value))
            return value;

        throw new ArgumentException("Value failed validation.", name);
    }
}