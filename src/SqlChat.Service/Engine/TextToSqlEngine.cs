using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;
using SqlChat.Service.Security;

namespace SqlChat.Service.Engine;

internal sealed class TextToSqlEngine(ISchemaProvider schemaProvider, ISqlGenerator generator, ISqlSecurityValidator? sqlSecurityValidator = null)
{
    private readonly ISchemaProvider _schemaProvider = schemaProvider.Validate();
    private readonly ISqlGenerator _generator = generator.Validate();
    private readonly ISqlSecurityValidator _sqlSecurity = sqlSecurityValidator ?? new SqlSecurityValidator();

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken ct = default) =>
        await _schemaProvider.GetSchemaAsync(ct).ConfigureAwait(false);

    // The built-in read-only checks always run. A caller-supplied validator can only add rules on top of
    // them, never replace them. (SqlSecurityValidator is sealed and runs the same checks itself.)
    public SqlValidationResult ValidateGeneratedSql(string sql)
    {
        if (_sqlSecurity is not SqlSecurityValidator)
        {
            SqlValidationResult builtIn = sql is null
                ? SqlValidationResult.Fail("SQL is null.")
                : SqlHeuristicValidation.Validate(sql, int.MaxValue);
            if (!builtIn.IsValid)
                return builtIn;
        }

        return _sqlSecurity.ValidateGeneratedSql(sql!); // null was rejected above, or is rejected by SqlSecurityValidator
    }

    public void ValidateGeneratedSqlOrThrow(string sql)
    {
        var result = ValidateGeneratedSql(sql);
        if (result.IsValid)
            return;

        throw new SqlSecurityException(result.RejectionReason ?? "Generated SQL failed security validation.");
    }

    public async Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        var schema = await GetSchemaAsync(ct).ConfigureAwait(false);
        var sql = await _generator.GenerateAsync(naturalLanguage, schema, ct).ConfigureAwait(false);
        ValidateGeneratedSqlOrThrow(sql);
        return sql;
    }

    public async Task<(IEnumerable<T> Data, string Sql)> GenerateAndExecuteManyAsync<T>(
        string naturalLanguage,
        IQueryExecutor executor,
        int llmFixRetryMax,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        executor.Validate();
        var schema = await GetSchemaAsync(ct).ConfigureAwait(false);
        return await RunWithLlmRetryAsync(naturalLanguage, schema, executor.QueryAsync<T>, llmFixRetryMax, ct).ConfigureAwait(false);
    }

    public async Task<(T? Data, string Sql)> GenerateAndExecuteSingleAsync<T>(
        string naturalLanguage,
        IQueryExecutor executor,
        int llmFixRetryMax,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);
        executor.Validate();
        var schema = await GetSchemaAsync(ct).ConfigureAwait(false);
        return await RunWithLlmRetryAsync(naturalLanguage, schema, executor.QuerySingleAsync<T>, llmFixRetryMax, ct).ConfigureAwait(false);
    }

    // Each attempt generates SQL, validates it, and runs it. A security rejection or a database error is fed
    // back to the generator for another try. Cancellation and failures the SQL text cannot fix
    // (NonRetryableQueryException: connection, auth, permission, timeout) stop the loop at once.
    private async Task<(TResult result, string sql)> RunWithLlmRetryAsync<TResult>(
        string naturalLanguage,
        SchemaCatalog schema,
        Func<string, CancellationToken, Task<TResult>> execute,
        int llmFixRetryMax,
        CancellationToken cancellationToken)
    {
        int maxAttempts = Math.Max(llmFixRetryMax, 0) + 1;
        string prompt = naturalLanguage;

        for (int attempt = 1; ; attempt++)
        {
            bool isLastAttempt = attempt >= maxAttempts;
            var sql = await _generator.GenerateAsync(prompt, schema, cancellationToken).ConfigureAwait(false);

            var validation = ValidateGeneratedSql(sql);
            if (!validation.IsValid)
            {
                string reason = validation.RejectionReason ?? "Generated SQL failed security validation.";
                if (isLastAttempt)
                    throw new SqlSecurityException(reason);

                prompt = BuildRetryPrompt(naturalLanguage, sql, $"Rejected by the read-only SQL policy: {reason}");
                continue;
            }

            try
            {
                TResult result = await execute(sql, cancellationToken).ConfigureAwait(false);
                return (result, sql);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NonRetryableQueryException ex)
            {
                throw new SqlExecutionFailedException(sql, $"SQL execution failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                if (isLastAttempt)
                    throw new SqlExecutionFailedException(sql, $"SQL execution failed after {attempt} attempt(s): {ex.Message}", ex);

                prompt = BuildRetryPrompt(naturalLanguage, sql, ex.Message);
            }
        }
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
