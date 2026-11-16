namespace SqlChat.Service.Engine;

/// <summary>
/// Thrown by <see cref="TextToSqlEngine"/> when every execution attempt failed. Carries the last SQL that was
/// run (it passed security validation) so the client can surface it on the result.
/// </summary>
internal sealed class SqlExecutionFailedException(string sql, string message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public string Sql { get; } = sql;
}
