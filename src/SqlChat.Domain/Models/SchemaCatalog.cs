namespace SqlChat.Domain.Models;

/// <summary>
/// The full set of tables exposed to text-to-SQL, plus optional few-shot examples.
/// </summary>
public sealed record SchemaCatalog(
    IReadOnlyList<TableDescriptor> Tables,
    IReadOnlyList<ExampleQuery>? ExampleQueries = null)
{
    public static SchemaCatalog Empty { get; } = new(Array.Empty<TableDescriptor>());
}
