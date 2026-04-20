namespace SqlChat.Domain.Models;

/// <summary>
/// A logical table (schema optional) and its columns.
/// </summary>
public sealed record TableDescriptor(
    string Name,
    string? Schema,
    IReadOnlyList<ColumnDescriptor> Columns);

