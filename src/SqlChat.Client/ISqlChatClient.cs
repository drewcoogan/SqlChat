using SqlChat.Domain.Models;
using SqlChat.Domain.Results;

namespace SqlChat.Client;

public interface ISqlChatClient
{
    Task<SqlChatCollectionResult<T>> QueryAsync<T>(string naturalLanguage, CancellationToken ct = default);
    Task<SqlChatResult<T>> QuerySingleAsync<T>(string naturalLanguage, CancellationToken ct = default);
    Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken ct = default);
    SqlValidationResult ValidateGeneratedSql(string sql);
    void ValidateGeneratedSqlOrThrow(string sql);
}
