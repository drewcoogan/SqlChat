namespace SqlChat.Domain.Models;

public sealed record ColumnEnrichment(string? Description, List<string>? SemanticTags);