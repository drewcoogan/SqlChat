using Microsoft.Extensions.DependencyInjection;
using SqlChat.Client;
using SqlChat.Data.Execution;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;
using SqlChat.Domain.Providers;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Client;

public sealed class SqlServerDiBuilderTests
{
    private static readonly SchemaCatalog TestSchema = new SchemaCatalogBuilder()
        .AddTable("products", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("name", "nvarchar"))
        .Build();

    private static SqlChatDiBuilder WithMinimalDeps(SqlChatDiBuilder builder)
    {
        builder.Services.AddSingleton<ISchemaProvider>(new ManualSchemaProvider(TestSchema));
        builder.Services.AddSingleton<ISqlGenerator>(new FakeSqlGenerator("SELECT 1"));
        return builder;
    }

    // -------------------------------------------------------------------------
    // UseSqlServerConnectionString — registration
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSqlServerConnectionString_RegistersISchemaProvider()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseSqlServerConnectionString("Server=localhost;Database=test");

        Assert.NotNull(services.BuildServiceProvider().GetService<ISchemaProvider>());
    }

    [Fact]
    public void UseSqlServerConnectionString_RegistersIQueryExecutor()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseSqlServerConnectionString("Server=localhost;Database=test");

        Assert.NotNull(services.BuildServiceProvider().GetService<IQueryExecutor>());
    }

    [Fact]
    public void UseSqlServerConnectionString_RegistersSqlServerSchemaProvider_AsConcrete()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseSqlServerConnectionString("Server=localhost;Database=test");

        Assert.NotNull(services.BuildServiceProvider().GetService<SqlServerSchemaProvider>());
    }

    // -------------------------------------------------------------------------
    // Core resolution with SQL Server providers
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSqlChat_WithSqlServer_CanResolveSqlChatClient()
    {
        var services = new ServiceCollection();
        WithMinimalDeps(services.AddSqlChat());

        using var scope = services.BuildServiceProvider().CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<SqlChatClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddSqlChat_WithSqlServer_ISqlChatClient_ResolvesSameScopedInstance()
    {
        var services = new ServiceCollection();
        WithMinimalDeps(services.AddSqlChat());

        using var scope = services.BuildServiceProvider().CreateScope();
        var concrete = scope.ServiceProvider.GetRequiredService<SqlChatClient>();
        var iface = scope.ServiceProvider.GetRequiredService<ISqlChatClient>();

        Assert.Same(concrete, iface);
    }

    // -------------------------------------------------------------------------
    // Enrich — works with SQL Server provider
    // -------------------------------------------------------------------------

    [Fact]
    public void Enrich_AfterUseSqlServer_WrapsWithEnrichingSchemaProvider()
    {
        var services = new ServiceCollection();
        services.AddSqlChat()
            .UseSqlServerConnectionString("Server=localhost;Database=test")
            .Enrich(e => e.ExcludeTable("audit_log"));

        var schemaProvider = services.BuildServiceProvider().GetRequiredService<ISchemaProvider>();

        Assert.IsType<EnrichingSchemaProvider>(schemaProvider);
    }

    [Fact]
    public void Enrich_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSqlChat();
        var enrichment = new SchemaEnrichmentBuilder().ExcludeTable("audit_log").Build();

        Assert.Throws<InvalidOperationException>(() => builder.Enrich(enrichment));
    }

    // -------------------------------------------------------------------------
    // SqlChatOptions — dialect defaults to SqlServer
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSqlServerConnectionString_DefaultOptions_HasSqlServerDialect()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseSqlServerConnectionString("Server=localhost;Database=test");

        // IQueryExecutor is built with SqlServer dialect when no explicit options are registered.
        // Verify the executor is resolvable and is the SQL Server variant.
        var executor = services.BuildServiceProvider().GetService<IQueryExecutor>();

        Assert.IsType<SqlServerQueryExecutor>(executor);
    }

    // -------------------------------------------------------------------------
    // WithOptions — dialect carried into executor
    // -------------------------------------------------------------------------

    [Fact]
    public void WithOptions_BeforeUseSqlServer_UsesProvidedOptions()
    {
        var services = new ServiceCollection();
        services.AddSqlChat()
            .WithOptions(o =>
            {
                o.ConnectionString = "Server=localhost;Database=test";
                o.Dialect = SqlDialect.SqlServer;
                o.LlmFixRetryMax = 1;
            })
            .UseSqlServerConnectionString("Server=localhost;Database=test");

        var options = services.BuildServiceProvider().GetRequiredService<SqlChatOptions>();

        Assert.Equal(SqlDialect.SqlServer, options.Dialect);
        Assert.Equal(1, options.LlmFixRetryMax);
    }
}
