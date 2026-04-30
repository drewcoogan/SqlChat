namespace SqlChat.Domain.Models;

/// <summary>
/// Describes a column as the library needs it for schema-aware text-to-SQL (store type, nullability, keys, hints).
/// </summary>
public sealed record ColumnDescriptor(string Name, string StoreType,
    string? Description, bool IsNullable, bool IsPrimaryKey,
    ForeignKeyReference? ForeignKey, IReadOnlyList<string> SemanticTags)
{
    public static ColumnDescriptor Create(string name, string storeType,
        string? description = null, bool isNullable = true, bool isPrimaryKey = false,
        ForeignKeyReference? foreignKey = null, IEnumerable<string>? semanticTags = null) =>
        new(
            name, storeType,
            description, isNullable, isPrimaryKey,
            foreignKey, semanticTags is null ? [] : [.. semanticTags.Distinct(StringComparer.OrdinalIgnoreCase)]);
}

