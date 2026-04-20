namespace SqlChat.Domain.Models;

/// <summary>
/// The full set of tables exposed to text-to-SQL.
/// </summary>
public sealed record SchemaCatalog(IReadOnlyList<TableDescriptor> Tables)
{
    public static SchemaCatalog Empty { get; } = new(Array.Empty<TableDescriptor>());
}

