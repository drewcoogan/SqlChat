namespace SqlChat.Domain.Models;

/// <summary>
/// Result of validating a generated SQL string (e.g. SELECT-only policy).
/// </summary>
public readonly record struct SqlValidationResult(bool IsValid, string? RejectionReason)
{
    public static SqlValidationResult Ok() => new(true, null);

    public static SqlValidationResult Fail(string reason) =>
        new(false, reason ?? throw new ArgumentNullException(nameof(reason)));
}

