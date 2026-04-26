using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Fakes;

internal sealed class FakeSqlGenerator(string sql) : ISqlGenerator
{
    public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
        => Task.FromResult(sql);
}
