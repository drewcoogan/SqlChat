using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Fakes;

/// <summary>
/// Returns successive SQL strings on each call. Useful for testing the LLM retry loop
/// where the first attempt fails and a corrected SQL is generated on the next attempt.
/// Clamps to the last entry once all responses are exhausted.
/// </summary>
internal sealed class SequencedFakeSqlGenerator(params string[] responses) : ISqlGenerator
{
    private int _callCount;

    public int CallCount => _callCount;

    public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken cancellationToken = default)
    {
        string sql = responses[Math.Min(_callCount, responses.Length - 1)];
        _callCount++;
        return Task.FromResult(sql);
    }
}
