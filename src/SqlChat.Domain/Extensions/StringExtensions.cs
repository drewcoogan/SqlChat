namespace SqlChat.Domain.Extensions;

public static class StringExtensions
{
    public static bool IsEmpty(this string? val) => string.IsNullOrWhiteSpace(val);

    public static bool IsNotEmpty(this string? val) => !val.IsEmpty();
}