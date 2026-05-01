using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Results;
using SqlChat.Service.Engine;

namespace SqlChat.Client;

public sealed class SqlChatClient : ISqlChatClient
{
    private readonly TextToSqlEngine _engine;
    private readonly IQueryExecutor? _executor;
    private readonly int _llmFixRetryMax;

    internal SqlChatClient(TextToSqlEngine engine, IQueryExecutor? executor = null, int llmFixRetryMax = 2)
    {
        _engine = engine.Validate();
        _executor = executor;
        _llmFixRetryMax = llmFixRetryMax;
    }

    public async Task<SqlChatCollectionResult<T>> QueryAsync<T>(string naturalLanguage, CancellationToken ct = default)
    {
        if (_executor is null)
            return SqlChatCollectionResult<T>.Fail(
                "No query executor configured. Provide a connection string to enable query execution.");

        try
        {
            var (data, sql) = await _engine
                .GenerateAndExecuteManyAsync<T>(naturalLanguage, _executor, _llmFixRetryMax, ct)
                .ConfigureAwait(false);
            return SqlChatCollectionResult<T>.Success(data, sql);
        }
        catch (Exception ex)
        {
            return SqlChatCollectionResult<T>.Fail(ex.Message);
        }
    }

    public async Task<SqlChatResult<T>> QuerySingleAsync<T>(string naturalLanguage, CancellationToken ct = default)
    {
        if (_executor is null)
            return SqlChatResult<T>.Fail("No query executor configured. Provide a connection string to enable query execution.");

        try
        {
            var (data, sql) = await _engine
                .GenerateAndExecuteSingleAsync<T>(naturalLanguage, _executor, _llmFixRetryMax, ct)
                .ConfigureAwait(false);
            return SqlChatResult<T>.Success(data, sql);
        }
        catch (Exception ex)
        {
            return SqlChatResult<T>.Fail(ex.Message);
        }
    }

    public Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken ct = default) =>
        _engine.GenerateSqlAsync(naturalLanguage, ct);

    public SqlValidationResult ValidateGeneratedSql(string sql) =>
        _engine.ValidateGeneratedSql(sql);

    public void ValidateGeneratedSqlOrThrow(string sql) =>
        _engine.ValidateGeneratedSqlOrThrow(sql);
}
