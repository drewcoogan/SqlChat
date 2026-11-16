namespace SqlChat.Domain.Exceptions;

/// <summary>
/// Thrown by an <c>IQueryExecutor</c> when a query failed for a reason that rewriting the SQL cannot fix
/// (connection, authentication, permission, timeout). The engine does not ask the LLM to retry these.
/// </summary>
public sealed class NonRetryableQueryException(string message, Exception innerException)
    : Exception(message, innerException);
