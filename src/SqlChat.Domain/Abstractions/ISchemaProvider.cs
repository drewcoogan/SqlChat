namespace SqlChat.Domain.Abstractions;

using SqlChat.Domain.Models;

/// <summary>
/// Supplies a <see cref="SchemaCatalog"/> (from EF, manual configuration, or another source).
/// </summary>
public interface ISchemaProvider
{
    Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default);
}

