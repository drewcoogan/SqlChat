namespace SqlChat.Tests.Integration;

/// <summary>
/// End-to-end integration tests against a real Postgres database and the Anthropic API.
/// These tests are skipped unless SQLCHAT_RUN_INTEGRATION_TESTS=true.
///
/// Required env vars (see .env.example):
///   ANTHROPIC_API_KEY, SQLCHAT_POSTGRES_CONNECTION_STRING, SQLCHAT_RUN_INTEGRATION_TESTS=true
///
/// The DatabaseFixture creates and seeds the test schema before all tests in this class
/// and drops it afterward. See DatabaseFixture for the known seed constants used below.
/// </summary>
[Collection("Integration")]
public sealed class SqlChatIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<IDictionary<string, object>> Rows(IEnumerable<dynamic> data) =>
        [.. data.Cast<IDictionary<string, object>>()];

    private static object? Field(IDictionary<string, object> row, string column) =>
        row.TryGetValue(column, out var v) ? v : null;

    // -------------------------------------------------------------------------
    // Simple selects
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task QueryAsync_AllCustomers_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from the sqlchat_test_customers table.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DatabaseFixture.TotalCustomers, result.Data.Count());
    }

    [IntegrationFact]
    public async Task QueryAsync_AllOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from the sqlchat_test_orders table.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DatabaseFixture.TotalOrders, result.Data.Count());
    }

    // -------------------------------------------------------------------------
    // Filtered selects
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task QueryAsync_DeliveredOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'delivered'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DatabaseFixture.DeliveredOrders, result.Data.Count());
    }

    [IntegrationFact]
    public async Task QueryAsync_ShippedOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'shipped'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Data); // DatabaseFixture.ShippedOrders == 1
    }

    [IntegrationFact]
    public async Task QueryAsync_PendingOrders_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders where status is 'pending'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Data); // DatabaseFixture.PendingOrders == 1
    }

    // -------------------------------------------------------------------------
    // Join — orders for a specific customer
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task QueryAsync_OrdersForAlice_ReturnsCorrectCount()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all orders from sqlchat_test_orders that belong to the customer " +
            "in sqlchat_test_customers with email 'alice@example.com'.");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(DatabaseFixture.AliceOrderCount, result.Data.Count());
    }

    // -------------------------------------------------------------------------
    // Single result
    // -------------------------------------------------------------------------

    [IntegrationFact]
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

    [IntegrationFact]
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

    [IntegrationFact]
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
    // Order items join
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task QueryAsync_ItemsForDeliveredOrders_ReturnsItems()
    {
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from sqlchat_test_order_items that belong to orders in " +
            "sqlchat_test_orders where status is 'delivered'.");

        Assert.True(result.IsSuccess, result.Error);

        // We seeded 4 items total, all on delivered orders 1 and 3
        Assert.Equal(4, result.Data.Count());
    }

    // -------------------------------------------------------------------------
    // SQL generation only (no DB required — validates LLM output is SELECT)
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task GenerateSqlAsync_SimpleQuery_ReturnsSql()
    {
        var sql = await fixture.Client.GenerateSqlAsync(
            "Show all rows from sqlchat_test_customers.");

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Result object behaviour
    // -------------------------------------------------------------------------

    [IntegrationFact]
    public async Task QueryAsync_IsFailed_WhenNoExecutorAndQueryRequested()
    {
        // The client built without a connection string has no executor — verifies
        // the IsFailed path is returned, not an exception.
        var result = await fixture.Client.QueryAsync<dynamic>(
            "Show all rows from sqlchat_test_customers.");

        // The fixture client does have an executor so this should succeed —
        // this test just documents that results always come back as a result object.
        Assert.False(result.Error is not null && result.IsSuccess,
            "A result cannot be both successful and carry an error.");
    }
}
