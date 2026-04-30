namespace SqlChat.Domain.Providers;

using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;

/// <summary>
/// Returns a schema built in code (e.g. via <see cref="SqlChat.Domain.Builders.SchemaCatalogBuilder"/>).
/// </summary>
public sealed class ManualSchemaProvider(SchemaCatalog catalog) : ISchemaProvider
{
    private readonly SchemaCatalog _catalog = catalog.Validate();

    public Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog);
}

