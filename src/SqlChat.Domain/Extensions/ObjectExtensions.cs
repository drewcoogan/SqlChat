using System.Runtime.CompilerServices;

namespace SqlChat.Domain.Extensions;

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

        throw new ArgumentException(name);
    }
}