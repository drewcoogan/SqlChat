namespace SqlChat.Domain.Exceptions;

/// <summary>
/// Thrown when generated SQL fails security validation (e.g. not SELECT-only).
/// </summary>
public sealed class SqlSecurityException : Exception
{
    public SqlSecurityException(string message) : base(message)
    {
    }

    public SqlSecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

