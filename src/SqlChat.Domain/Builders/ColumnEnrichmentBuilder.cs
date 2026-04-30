using SqlChat.Domain.Models;

namespace SqlChat.Domain.Builders;

public sealed class ColumnEnrichmentBuilder
{
    private string? _description;
    private readonly List<string> _tags = [];

    public ColumnEnrichmentBuilder Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
        return this;
    }

    public ColumnEnrichmentBuilder Tag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        _tags.Add(tag);
        return this;
    }

    internal ColumnEnrichment Build() => new(_description, _tags.Count > 0 ? [.. _tags] : null);
}
