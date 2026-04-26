namespace SqlChat.Domain.Models;

public sealed record ColumnEnrichment
{
    public string? Description { get; init; }
    public List<string>? SemanticTags { get; init; }
}
