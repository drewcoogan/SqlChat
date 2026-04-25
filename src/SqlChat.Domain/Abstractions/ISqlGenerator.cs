using SqlChat.Domain.Models;

namespace SqlChat.Domain.Abstractions;

public interface ISqlGenerator
{
    Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default);
}
