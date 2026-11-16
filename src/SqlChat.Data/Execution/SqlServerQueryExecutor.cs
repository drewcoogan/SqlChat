using Dapper;
using Microsoft.Data.SqlClient;
using Polly;
using SqlChat.Data.Internal;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Options;

namespace SqlChat.Data.Execution;

public sealed class SqlServerQueryExecutor(SqlChatOptions options) : IQueryExecutor
{
    private const int TimeoutErrorNumber = -2;

    private readonly string _connectionString = options
        .Validate(nameof(options))
        .ConnectionString
        .Validate(v => v.IsNotEmpty())!;
    private readonly ResiliencePipeline _pipeline =
        QueryRetryPipeline.Build<SqlException>(options.QueryRetryMax, ex => ex.IsTransient);

    public Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (connection, ct) => connection.QueryAsync<T>(new CommandDefinition(sql, cancellationToken: ct)),
            cancellationToken);

    public Task<T?> QuerySingleAsync<T>(string sql, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            (connection, ct) => connection.QuerySingleOrDefaultAsync<T?>(new CommandDefinition(sql, cancellationToken: ct)),
            cancellationToken);

    // The retry wraps connect → query: a transient failure usually breaks the connection, so retrying the
    // query on the same connection could never succeed.
    private async Task<TResult> ExecuteAsync<TResult>(
        Func<SqlConnection, CancellationToken, Task<TResult>> query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return await query(connection, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (!IsSqlTextError(ex.Class, ex.Number))
        {
            throw new NonRetryableQueryException(ex.Message, ex);
        }
    }

    // Severity classes a rewritten query can fix: 11 (object not found), 15 (syntax) and 16 (general user
    // error such as a bad conversion). Class 14 is permissions; 17+ is resource or server trouble. A client
    // timeout is reported as class 11 with number -2, so it is excluded by number.
    internal static bool IsSqlTextError(byte errorClass, int errorNumber) =>
        errorNumber != TimeoutErrorNumber && errorClass is 11 or 15 or 16;
}
