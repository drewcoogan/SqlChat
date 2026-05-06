using Npgsql;
using SqlChat.Ai.Generators;
using SqlChat.Client;
using SqlChat.Domain.Options;

namespace SqlChat.Tests.Integration;

/// <summary>
/// Creates a deterministic test schema and seeds it with known data before any integration
/// test runs. The schema is dropped on disposal. All tests in a class share one fixture instance.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // Known seed facts — used to write deterministic assertions.
    public const int TotalCustomers = 3;
    public const int TotalOrders = 6;
    public const int DeliveredOrders = 2;
    public const int ShippedOrders = 1;
    public const int PendingOrders = 1;
    public const int AliceOrderCount = 2;

    // All order totals in cents: 5000 + 3500 + 7500 + 2000 + 1500 + 4000
    public const int TotalRevenueCents = 23500;

    // Delivered orders only: 5000 + 7500
    public const int DeliveredRevenueCents = 12500;

    public SqlChatClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (IntegrationConfig.IsEnabled)
        {
            await CreateSchemaAsync();
            await SeedDataAsync();
            Client = BuildClient();
        }
    }

    public async Task DisposeAsync()
    {
        if (IntegrationConfig.IsEnabled)
        {
            await DropSchemaAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Schema creation / teardown
    // -------------------------------------------------------------------------

    private static async Task CreateSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(IntegrationConfig.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sqlchat_test_customers (
                id         SERIAL PRIMARY KEY,
                first_name TEXT        NOT NULL,
                last_name  TEXT        NOT NULL,
                email      TEXT        NOT NULL UNIQUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS sqlchat_test_orders (
                id          SERIAL PRIMARY KEY,
                customer_id INT         NOT NULL REFERENCES sqlchat_test_customers(id),
                status      TEXT        NOT NULL,
                total_cents INT         NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS sqlchat_test_order_items (
                id               SERIAL PRIMARY KEY,
                order_id         INT  NOT NULL REFERENCES sqlchat_test_orders(id),
                product_name     TEXT NOT NULL,
                quantity         INT  NOT NULL,
                unit_price_cents INT  NOT NULL
            );

            TRUNCATE sqlchat_test_order_items, sqlchat_test_orders, sqlchat_test_customers RESTART IDENTITY CASCADE;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(IntegrationConfig.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP TABLE IF EXISTS sqlchat_test_order_items;
            DROP TABLE IF EXISTS sqlchat_test_orders;
            DROP TABLE IF EXISTS sqlchat_test_customers;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Seed data — deterministic, matches the constants above
    // -------------------------------------------------------------------------

    private static async Task SeedDataAsync()
    {
        await using var conn = new NpgsqlConnection(IntegrationConfig.ConnectionString);
        await conn.OpenAsync();

        // Customers
        await using var customerCmd = conn.CreateCommand();
        customerCmd.CommandText = """
            INSERT INTO sqlchat_test_customers (first_name, last_name, email) VALUES
                ('Alice', 'Smith',  'alice@example.com'),
                ('Bob',   'Jones',  'bob@example.com'),
                ('Carol', 'White',  'carol@example.com');
            """;
        await customerCmd.ExecuteNonQueryAsync();

        // Orders  (status: pending | processing | shipped | delivered | cancelled)
        await using var orderCmd = conn.CreateCommand();
        orderCmd.CommandText = """
            INSERT INTO sqlchat_test_orders (customer_id, status, total_cents) VALUES
                (1, 'delivered',  5000),   -- Alice,  $50.00
                (1, 'shipped',    3500),   -- Alice,  $35.00
                (2, 'delivered',  7500),   -- Bob,    $75.00
                (2, 'cancelled',  2000),   -- Bob,    $20.00
                (3, 'pending',    1500),   -- Carol,  $15.00
                (3, 'processing', 4000);   -- Carol,  $40.00
            """;
        await orderCmd.ExecuteNonQueryAsync();

        // Order items — only for the two delivered orders so we can assert totals
        await using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = """
            INSERT INTO sqlchat_test_order_items (order_id, product_name, quantity, unit_price_cents) VALUES
                (1, 'Widget A', 2, 1500),   -- $30.00
                (1, 'Widget B', 1, 2000),   -- $20.00  → order 1 total = $50.00 ✓
                (3, 'Gadget X', 1, 4500),   -- $45.00
                (3, 'Gadget Y', 1, 3000);   -- $30.00  → order 3 total = $75.00 ✓
            """;
        await itemCmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Client construction
    // -------------------------------------------------------------------------

    private static SqlChatClient BuildClient() =>
        new SqlChatBuilder()
            .UseConnectionString(IntegrationConfig.ConnectionString, IntegrationConfig.QueryRetryMax)
            .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
            {
                AnthropicApiKey  = IntegrationConfig.AnthropicApiKey,
                AnthropicModel   = IntegrationConfig.AnthropicModel,
                MaxOutputTokens  = IntegrationConfig.AnthropicMaxOutputTokens,
                LlmFixRetryMax   = IntegrationConfig.LlmFixRetryMax
            }))
            .Enrich(e => e
                .Table("sqlchat_test_customers", t => t
                    .Description("Registered customers."))
                .Table("sqlchat_test_orders", t => t
                    .Description("Purchase orders placed by customers.")
                    .Column("status", "One of: pending, processing, shipped, delivered, cancelled.")
                    .Column("total_cents", "Order total in US cents. Divide by 100 for dollars.")
                    .Column("customer_id", "Foreign key to sqlchat_test_customers.id."))
                .Table("sqlchat_test_order_items", t => t
                    .Description("Individual line items within an order.")
                    .Column("unit_price_cents", "Unit price in US cents.")
                    .Column("order_id", "Foreign key to sqlchat_test_orders.id."))
                .AddExample(
                    "Show all customers",
                    "SELECT id, first_name, last_name, email, created_at FROM sqlchat_test_customers")
                .AddExample(
                    "Show all delivered orders",
                    "SELECT id, customer_id, status, total_cents, created_at FROM sqlchat_test_orders WHERE status = 'delivered'")
                .AddExample(
                    "Show all orders for a customer by email",
                    "SELECT o.id, o.status, o.total_cents, o.created_at FROM sqlchat_test_orders o JOIN sqlchat_test_customers c ON c.id = o.customer_id WHERE c.email = 'example@example.com'"))
            .Build();
}
