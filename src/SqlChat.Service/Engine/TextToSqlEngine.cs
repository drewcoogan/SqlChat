using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Service.Security;

namespace SqlChat.Service.Engine;

public sealed class TextToSqlEngine
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly ISqlSecurityValidator _sqlSecurity;
    private readonly ISqlGenerator _generator;

    public TextToSqlEngine(
        ISchemaProvider schemaProvider,
        ISqlGenerator generator,
        ISqlSecurityValidator? sqlSecurityValidator = null)
    {
        _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _sqlSecurity = sqlSecurityValidator ?? new SqlSecurityValidator();
    }

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        await _schemaProvider.GetSchemaAsync(cancellationToken).ConfigureAwait(false);

    public SqlValidationResult ValidateGeneratedSql(string sql) =>
        _sqlSecurity.ValidateGeneratedSql(sql);

    public void ValidateGeneratedSqlOrThrow(string sql)
    {
        var result = ValidateGeneratedSql(sql);
        if (result.IsValid)
            return;

        throw new SqlSecurityException(result.RejectionReason ?? "Generated SQL failed security validation.");
    }

    public async Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);

        var schema = await GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        var sql = await _generator.GenerateAsync(naturalLanguage, schema, cancellationToken).ConfigureAwait(false);
        ValidateGeneratedSqlOrThrow(sql);
        return sql;
    }

    public async Task<(IEnumerable<T> Data, string Sql)> GenerateAndExecuteManyAsync<T>(
        string naturalLanguage,
        IQueryExecutor executor,
        int llmFixRetryMax,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        ArgumentNullException.ThrowIfNull(executor);

        var schema = await GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        return await RunWithLlmRetryAsync(
            naturalLanguage,
            schema,
            (sql, ct) => executor.QueryAsync<T>(sql, ct),
            llmFixRetryMax,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<(T? Data, string Sql)> GenerateAndExecuteSingleAsync<T>(
        string naturalLanguage,
        IQueryExecutor executor,
        int llmFixRetryMax,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        ArgumentNullException.ThrowIfNull(executor);

        var schema = await GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        return await RunWithLlmRetryAsync(
            naturalLanguage,
            schema,
            (sql, ct) => executor.QuerySingleAsync<T>(sql, ct),
            llmFixRetryMax,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(TResult result, string sql)> RunWithLlmRetryAsync<TResult>(
        string naturalLanguage,
        SchemaCatalog schema,
        Func<string, CancellationToken, Task<TResult>> execute,
        int llmFixRetryMax,
        CancellationToken cancellationToken)
    {
        string prompt = naturalLanguage;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= llmFixRetryMax; attempt++)
        {
            var sql = await _generator.GenerateAsync(prompt, schema, cancellationToken).ConfigureAwait(false);

            var validation = ValidateGeneratedSql(sql);
            if (!validation.IsValid)
                throw new SqlSecurityException(validation.RejectionReason ?? "Generated SQL failed security validation.");

            try
            {
                TResult result = await execute(sql, cancellationToken).ConfigureAwait(false);
                return (result, sql);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < llmFixRetryMax)
                    prompt = BuildRetryPrompt(naturalLanguage, sql, ex.Message);
            }
        }

        throw new InvalidOperationException(
            $"SQL execution failed after {llmFixRetryMax + 1} attempt(s).", lastException);
    }

    private static string BuildRetryPrompt(string originalQuestion, string failedSql, string error) =>
        $"""
        The following SQL was generated for this question but failed with an error.

        Original question: {originalQuestion}

        Failed SQL:
        {failedSql}

        Error:
        {error}

        Please fix the SQL to correctly answer the original question.
        """;
}
