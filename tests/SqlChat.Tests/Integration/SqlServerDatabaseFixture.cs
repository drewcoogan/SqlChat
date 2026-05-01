using Microsoft.Data.SqlClient;
using SqlChat.Ai.Generators;
using SqlChat.Client;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;

namespace SqlChat.Tests.Integration;

public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public const int TotalCustomers = 3;
    public const int TotalOrders = 6;
    public const int DeliveredOrders = 2;
    public const int ShippedOrders = 1;
    public const int PendingOrders = 1;
    public const int AliceOrderCount = 2;

    public SqlChatClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (IntegrationConfig.IsSqlServerEnabled)
        {
            await EnsureDatabaseAsync();
            await CreateSchemaAsync();
            await SeedDataAsync();
            Client = BuildClient();
        }
    }

    public async Task DisposeAsync()
    {
        if (IntegrationConfig.IsSqlServerEnabled)
            await DropSchemaAsync();
    }

    // -------------------------------------------------------------------------
    // Database / schema lifecycle
    // -------------------------------------------------------------------------

    private static async Task EnsureDatabaseAsync()
    {
        var builder = new SqlConnectionStringBuilder(IntegrationConfig.SqlServerConnectionString)
        {
            InitialCatalog = "master"
        };

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'sqlchat_test')
                CREATE DATABASE sqlchat_test;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateSchemaAsync()
    {
        await using var conn = new SqlConnection(IntegrationConfig.SqlServerConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID('sqlchat_test_order_items', 'U') IS NOT NULL DROP TABLE sqlchat_test_order_items;
            IF OBJECT_ID('sqlchat_test_orders',      'U') IS NOT NULL DROP TABLE sqlchat_test_orders;
            IF OBJECT_ID('sqlchat_test_customers',   'U') IS NOT NULL DROP TABLE sqlchat_test_customers;

            CREATE TABLE sqlchat_test_customers (
                id         INT            IDENTITY(1,1) PRIMARY KEY,
                first_name NVARCHAR(100)  NOT NULL,
                last_name  NVARCHAR(100)  NOT NULL,
                email      NVARCHAR(255)  NOT NULL UNIQUE,
                created_at DATETIME2      NOT NULL DEFAULT GETUTCDATE()
            );

            CREATE TABLE sqlchat_test_orders (
                id          INT            IDENTITY(1,1) PRIMARY KEY,
                customer_id INT            NOT NULL REFERENCES sqlchat_test_customers(id),
                status      NVARCHAR(50)   NOT NULL,
                total_cents INT            NOT NULL,
                created_at  DATETIME2      NOT NULL DEFAULT GETUTCDATE()
            );

            CREATE TABLE sqlchat_test_order_items (
                id               INT            IDENTITY(1,1) PRIMARY KEY,
                order_id         INT            NOT NULL REFERENCES sqlchat_test_orders(id),
                product_name     NVARCHAR(255)  NOT NULL,
                quantity         INT            NOT NULL,
                unit_price_cents INT            NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropSchemaAsync()
    {
        await using var conn = new SqlConnection(IntegrationConfig.SqlServerConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID('sqlchat_test_order_items', 'U') IS NOT NULL DROP TABLE sqlchat_test_order_items;
            IF OBJECT_ID('sqlchat_test_orders',      'U') IS NOT NULL DROP TABLE sqlchat_test_orders;
            IF OBJECT_ID('sqlchat_test_customers',   'U') IS NOT NULL DROP TABLE sqlchat_test_customers;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Seed data
    // -------------------------------------------------------------------------

    private static async Task SeedDataAsync()
    {
        await using var conn = new SqlConnection(IntegrationConfig.SqlServerConnectionString);
        await conn.OpenAsync();

        await using var customerCmd = conn.CreateCommand();
        customerCmd.CommandText = """
            INSERT INTO sqlchat_test_customers (first_name, last_name, email) VALUES
                (N'Alice', N'Smith', N'alice@example.com'),
                (N'Bob',   N'Jones', N'bob@example.com'),
                (N'Carol', N'White', N'carol@example.com');
            """;
        await customerCmd.ExecuteNonQueryAsync();

        await using var orderCmd = conn.CreateCommand();
        orderCmd.CommandText = """
            INSERT INTO sqlchat_test_orders (customer_id, status, total_cents) VALUES
                (1, N'delivered',  5000),
                (1, N'shipped',    3500),
                (2, N'delivered',  7500),
                (2, N'cancelled',  2000),
                (3, N'pending',    1500),
                (3, N'processing', 4000);
            """;
        await orderCmd.ExecuteNonQueryAsync();

        await using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = """
            INSERT INTO sqlchat_test_order_items (order_id, product_name, quantity, unit_price_cents) VALUES
                (1, N'Widget A', 2, 1500),
                (1, N'Widget B', 1, 2000),
                (3, N'Gadget X', 1, 4500),
                (3, N'Gadget Y', 1, 3000);
            """;
        await itemCmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------------
    // Client construction
    // -------------------------------------------------------------------------

    private static SqlChatClient BuildClient() =>
        new SqlChatBuilder()
            .UseSqlServerConnectionString(IntegrationConfig.SqlServerConnectionString, IntegrationConfig.QueryRetryMax)
            .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
            {
                AnthropicApiKey = IntegrationConfig.AnthropicApiKey,
                AnthropicModel  = IntegrationConfig.AnthropicModel,
                Dialect         = SqlDialect.SqlServer,
                LlmFixRetryMax  = IntegrationConfig.LlmFixRetryMax
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
