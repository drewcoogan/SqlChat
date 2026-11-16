using Npgsql;
using SqlChat.Data.Execution;
using SqlChat.Domain.Options;

namespace SqlChat.Tests.Data;

public sealed class QueryExecutorTests
{
    private static SqlChatOptions Options(int queryRetryMax) =>
        new() { ConnectionString = "Host=localhost;Database=test", QueryRetryMax = queryRetryMax };

    [Fact]
    public void Constructor_ZeroQueryRetryMax_DisablesRetryWithoutThrowing()
    {
        var postgres = new PostgresQueryExecutor(Options(0));
        var sqlServer = new SqlServerQueryExecutor(Options(0));

        Assert.NotNull(postgres);
        Assert.NotNull(sqlServer);
    }

    [Fact]
    public void Constructor_NegativeQueryRetryMax_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgresQueryExecutor(Options(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqlServerQueryExecutor(Options(-1)));
    }

    [Theory]
    [InlineData("42601", true)]   // syntax error
    [InlineData("42703", true)]   // undefined column
    [InlineData("42P01", true)]   // undefined table
    [InlineData("22012", true)]   // division by zero
    [InlineData("42501", false)]  // insufficient privilege
    [InlineData("28P01", false)]  // invalid password
    [InlineData("57014", false)]  // query cancelled / statement timeout
    [InlineData("25006", false)]  // read-only transaction violation
    public void PostgresIsSqlTextError_BySqlState_ClassifiesFixableErrors(string sqlState, bool expected)
    {
        var exception = new PostgresException("message", "ERROR", "ERROR", sqlState);

        Assert.Equal(expected, PostgresQueryExecutor.IsSqlTextError(exception));
    }

    [Fact]
    public void PostgresIsSqlTextError_ConnectionFailure_ReturnsFalse()
    {
        Assert.False(PostgresQueryExecutor.IsSqlTextError(new NpgsqlException("connection refused")));
    }

    [Theory]
    [InlineData(15, 102, true)]    // incorrect syntax
    [InlineData(16, 207, true)]    // invalid column name
    [InlineData(16, 208, true)]    // invalid object name
    [InlineData(11, -2, false)]    // client timeout
    [InlineData(14, 229, false)]   // permission denied
    [InlineData(14, 18456, false)] // login failed
    [InlineData(20, 233, false)]   // connection broken
    public void SqlServerIsSqlTextError_ByClassAndNumber_ClassifiesFixableErrors(byte errorClass, int number, bool expected)
    {
        Assert.Equal(expected, SqlServerQueryExecutor.IsSqlTextError(errorClass, number));
    }
}
