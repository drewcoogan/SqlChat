namespace SqlChat.Domain.Models;

public sealed record SchemaEnrichment
{
    public List<string> ExcludedTables { get; init; } = [];
    public Dictionary<string, TableEnrichment> Tables { get; init; } = [];
}
