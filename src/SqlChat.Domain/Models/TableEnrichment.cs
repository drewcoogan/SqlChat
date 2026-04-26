namespace SqlChat.Domain.Models;

public sealed record TableEnrichment
{
    public string? Description { get; init; }
    public List<string> ExcludedColumns { get; init; } = [];
    public Dictionary<string, ColumnEnrichment> Columns { get; init; } = [];
}
