using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

namespace SqlChat.Domain.Builders;

public sealed class ColumnEnrichmentBuilder
{
    private string? _description;
    private readonly List<string> _tags = [];

    public ColumnEnrichmentBuilder Description(string description)
    {
        _description = description.Validate(x => x.IsNotEmpty());
        return this;
    }

    public ColumnEnrichmentBuilder Tag(string tag)
    {
        _tags.Add(tag.Validate(x => x.IsNotEmpty()));
        return this;
    }

    internal ColumnEnrichment Build() => new(_description, _tags.Count > 0 ? [.. _tags] : null);
}
