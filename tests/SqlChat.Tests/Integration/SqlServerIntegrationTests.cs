namespace SqlChat.Tests.Integration;

[Collection("SqlServerIntegration")]
public sealed class SqlServerIntegrationTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private static List<IDictionary<string, object>> Rows(IEnumerable<dynamic> data) =>
        [.. data.Cast<IDictionary<string, object>>()];

    private static object? Field(IDictionary<string, object> row, string column) =>
        row.TryGetValue(column, out var v) ? v : null;

    // -------------------------------------------------------------------------
    // Simple selects
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task QueryAsync_AllCustomers_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from the sqlchat_test_customers table.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SqlServerDatabaseFixture.TotalCustomers, result.Data.Count());
    }

    [SqlServerIntegrationFact]
    public async Task QueryAsync_AllOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from the sqlchat_test_orders table.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SqlServerDatabaseFixture.TotalOrders, result.Data.Count());
    }

    // -------------------------------------------------------------------------
    // Filtered selects
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task QueryAsync_DeliveredOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'delivered'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SqlServerDatabaseFixture.DeliveredOrders, result.Data.Count());
    }

    [SqlServerIntegrationFact]
    public async Task QueryAsync_PendingOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'pending'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Data);
    }

    // -------------------------------------------------------------------------
    // Join
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task QueryAsync_OrdersForAlice_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders that belong to the customer " +
            "in sqlchat_test_customers with email 'alice@example.com'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SqlServerDatabaseFixture.AliceOrderCount, result.Data.Count());
    }

    // -------------------------------------------------------------------------
    // Single result
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task QuerySingleAsync_CustomerByEmail_ReturnsCustomer()
    {
        var result = await fixture.Client.QuerySingleAsync<dynamic>(
            "Find the single customer from sqlchat_test_customers with email 'carol@example.com'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Data);

        var row = (IDictionary<string, object>)result.Data!;
        Assert.Equal("carol@example.com", Field(row, "email")?.ToString());
    }

    // -------------------------------------------------------------------------
    // Field-value assertions
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task QueryAsync_AllCustomers_ContainsExpectedEmails()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from the sqlchat_test_customers table.");

        Assert.True(result.IsSuccess, result.Error);

        var emails = Rows(result.Data)
            .Select(r => Field(r, "email")?.ToString())
            .ToHashSet();

        Assert.Contains("alice@example.com", emails);
        Assert.Contains("bob@example.com", emails);
        Assert.Contains("carol@example.com", emails);
    }

    [SqlServerIntegrationFact]
    public async Task QueryAsync_DeliveredOrders_AllHaveCorrectStatus()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'delivered'.");

        Assert.True(result.IsSuccess, result.Error);

        var rows = Rows(result.Data);
        Assert.All(rows, row =>
            Assert.Equal("delivered", Field(row, "status")?.ToString()));
    }

    // -------------------------------------------------------------------------
    // SQL generation only
    // -------------------------------------------------------------------------

    [SqlServerIntegrationFact]
    public async Task GenerateSqlAsync_SimpleQuery_ReturnsTSql()
    {
        var sql = await fixture.Client.GenerateSqlAsync(
            "Show all rows from sqlchat_test_customers.");

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
    }
}
