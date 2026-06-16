using Microsoft.Extensions.DependencyInjection;
using SqlChat.Client;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;
using SqlChat.Domain.Providers;
using SqlChat.Service.Security;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Client;

public sealed class SqlChatDiBuilderTests
{
    private static readonly SchemaCatalog TestSchema = new SchemaCatalogBuilder()
        .AddTable("users", t => t
            .AddColumn("id", "int", primaryKey: true)
            .AddColumn("email", "varchar"))
        .Build();

    private static SqlChatDiBuilder WithMinimalDeps(SqlChatDiBuilder builder)
    {
        builder.Services.AddSingleton<ISchemaProvider>(new ManualSchemaProvider(TestSchema));
        builder.Services.AddSingleton<ISqlGenerator>(new FakeSqlGenerator("SELECT 1"));
        return builder;
    }

    // -------------------------------------------------------------------------
    // Core registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSqlChat_CanResolveSqlChatClient_WhenDepsRegistered()
    {
        var services = new ServiceCollection();
        WithMinimalDeps(services.AddSqlChat());

        using var scope = services.BuildServiceProvider().CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<SqlChatClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddSqlChat_ISqlChatClient_ResolvesSameScopedInstance_AsSqlChatClient()
    {
        var services = new ServiceCollection();
        WithMinimalDeps(services.AddSqlChat());

        using var scope = services.BuildServiceProvider().CreateScope();
        var concrete = scope.ServiceProvider.GetRequiredService<SqlChatClient>();
        var iface = scope.ServiceProvider.GetRequiredService<ISqlChatClient>();

        Assert.Same(concrete, iface);
    }

    // -------------------------------------------------------------------------
    // UseGenerator
    // -------------------------------------------------------------------------

    [Fact]
    public void UseGenerator_RegistersISqlGenerator()
    {
        var services = new ServiceCollection();
        var generator = new FakeSqlGenerator("SELECT 1");
        services.AddSqlChat().UseGenerator(generator);

        var resolved = services.BuildServiceProvider().GetRequiredService<ISqlGenerator>();

        Assert.Same(generator, resolved);
    }

    // -------------------------------------------------------------------------
    // UsePostgresConnectionString
    // -------------------------------------------------------------------------

    [Fact]
    public void UsePostgresConnectionString_RegistersISchemaProvider()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UsePostgresConnectionString("Host=localhost;Database=test");

        Assert.NotNull(services.BuildServiceProvider().GetService<ISchemaProvider>());
    }

    [Fact]
    public void UsePostgresConnectionString_RegistersIQueryExecutor()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UsePostgresConnectionString("Host=localhost;Database=test");

        Assert.NotNull(services.BuildServiceProvider().GetService<IQueryExecutor>());
    }

    // -------------------------------------------------------------------------
    // WithOptions
    // -------------------------------------------------------------------------

    [Fact]
    public void WithOptions_RegistersSqlChatOptions_WithConfiguredValues()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().WithOptions(o => o.LlmFixRetryMax = 5);

        var options = services.BuildServiceProvider().GetRequiredService<SqlChatOptions>();

        Assert.Equal(5, options.LlmFixRetryMax);
    }

    // -------------------------------------------------------------------------
    // UseSqlSecurity
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSqlSecurity_RegistersCustomSecurityOptions()
    {
        var services = new ServiceCollection();
        services.AddSqlChat().UseSqlSecurity(new SqlSecurityOptions { MaxSqlLength = 1024 });

        var resolved = services.BuildServiceProvider().GetRequiredService<SqlSecurityOptions>();

        Assert.Equal(1024, resolved.MaxSqlLength);
    }

    // -------------------------------------------------------------------------
    // Enrich
    // -------------------------------------------------------------------------

    [Fact]
    public void Enrich_Action_WrapsSchemaProvider_WithEnrichingSchemaProvider()
    {
        var services = new ServiceCollection();
        services.AddSqlChat()
            .UsePostgresConnectionString("Host=localhost;Database=test")
            .Enrich(e => e.ExcludeTable("audit_log"));

        var schemaProvider = services.BuildServiceProvider().GetRequiredService<ISchemaProvider>();

        Assert.IsType<EnrichingSchemaProvider>(schemaProvider);
    }

    [Fact]
    public void Enrich_SchemaEnrichment_RegistersEnrichmentWithExcludedTables()
    {
        var services = new ServiceCollection();
        var enrichment = new SchemaEnrichmentBuilder()
            .ExcludeTable("audit_log")
            .Build();

        services.AddSqlChat()
            .UsePostgresConnectionString("Host=localhost;Database=test")
            .Enrich(enrichment);

        var resolved = services.BuildServiceProvider().GetRequiredService<SchemaEnrichment>();

        Assert.Contains("audit_log", resolved.ExcludedTables);
    }

    // -------------------------------------------------------------------------
    // UseSchemaFromFile
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UseSchemaFromFile_JsonExtension_LoadsSchema()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(path, """
            {
              "tables": [
                {
                  "name": "orders",
                  "columns": [
                    { "name": "id", "storeType": "int", "isPrimaryKey": true, "isNullable": false },
                    { "name": "total", "storeType": "decimal", "isNullable": false }
                  ]
                }
              ]
            }
            """);

        try
        {
            var services = new ServiceCollection();
            services.AddSqlChat()
                .UseSchemaFromFile(path)
                .UseGenerator(new FakeSqlGenerator("SELECT 1"));

            var provider = services.BuildServiceProvider().GetRequiredService<ISchemaProvider>();
            var catalog = await provider.GetSchemaAsync();

            Assert.Single(catalog.Tables);
            Assert.Equal("orders", catalog.Tables[0].Name);
            Assert.Equal(2, catalog.Tables[0].Columns.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UseSchemaFromFile_YamlExtension_LoadsSchema()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".yaml");
        File.WriteAllText(path, """
            tables:
              - name: customers
                columns:
                  - name: id
                    storeType: int
                    isPrimaryKey: true
                    isNullable: false
                  - name: email
                    storeType: varchar
                    isNullable: false
            """);

        try
        {
            var services = new ServiceCollection();
            services.AddSqlChat()
                .UseSchemaFromFile(path)
                .UseGenerator(new FakeSqlGenerator("SELECT 1"));

            var provider = services.BuildServiceProvider().GetRequiredService<ISchemaProvider>();
            var catalog = await provider.GetSchemaAsync();

            Assert.Single(catalog.Tables);
            Assert.Equal("customers", catalog.Tables[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // -------------------------------------------------------------------------
    // EnrichFromFile
    // -------------------------------------------------------------------------

    [Fact]
    public void EnrichFromFile_LoadsJsonFile_AndRegistersExcludedTables()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(path, """{ "excludedTables": ["audit_log", "temp"] }""");

        try
        {
            var services = new ServiceCollection();
            services.AddSqlChat()
                .UsePostgresConnectionString("Host=localhost;Database=test")
                .EnrichFromFile(path);

            var enrichment = services.BuildServiceProvider().GetRequiredService<SchemaEnrichment>();

            Assert.Contains("audit_log", enrichment.ExcludedTables);
            Assert.Contains("temp", enrichment.ExcludedTables);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
