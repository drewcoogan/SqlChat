namespace SqlChat.Domain.Extensions;

/// <summary>String helpers used inside the SqlChat packages. Not intended for consumer code.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class StringExtensions
{
    public static bool IsEmpty(this string? val) => string.IsNullOrWhiteSpace(val);

    public static bool IsNotEmpty(this string? val) => !val.IsEmpty();
}