namespace SqlChat.Domain.Models;

/// <summary>
/// Describes a foreign-key relationship for a column (single-column FKs; composite keys can be modeled later).
/// </summary>
public sealed record ForeignKeyReference(
    string PrincipalTable,
    string? PrincipalSchema,
    string PrincipalColumn);

