using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlChat.Client;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;
using SqlChat.Tests.Fakes;

namespace SqlChat.Tests.Client;

public sealed class SqlChatBuilderEfSchemaTests
{
    private sealed class Blog
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Blog> Blogs => Set<Blog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>(b =>
            {
                b.ToTable("Blogs");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).HasColumnType("nvarchar(200)").IsRequired();
            });
        }
    }

    private static TestDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // -------------------------------------------------------------------------
    // SqlChatBuilder
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UseSchemaFromEF_WithInMemoryContext_SchemaContainsEfModelTables()
    {
        await using var db = CreateInMemoryContext();

        var client = new SqlChatBuilder()
            .UseSchemaFromEF(db)
            .UseGenerator(new FakeSqlGenerator("SELECT 1"))
            .Build();

        var sql = await client.GenerateSqlAsync("find all blogs");

        Assert.Equal("SELECT 1", sql);
    }

    [Fact]
    public async Task UseSchemaFromEF_WithEnrichment_ExcludedTableNotInSchema()
    {
        await using var db = CreateInMemoryContext();
        var gen = new CapturingGenerator();

        var client = new SqlChatBuilder()
            .UseSchemaFromEF(db)
            .UseGenerator(gen)
            .Enrich(e => e.ExcludeTable("Blogs"))
            .Build();

        await client.GenerateSqlAsync("find all blogs");

        Assert.Empty(gen.LastSchema!.Tables);
    }

    // -------------------------------------------------------------------------
    // SqlChatDiBuilder
    // -------------------------------------------------------------------------

    [Fact]
    public void UseSchemaFromEF_DiBuilder_RegistersEfCoreSchemaProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateInMemoryContext());
        services.AddSqlChat()
            .UseSchemaFromEF<TestDbContext>()
            .UseGenerator(new FakeSqlGenerator("SELECT 1"));

        using var scope = services.BuildServiceProvider().CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ISchemaProvider>();
        Assert.IsType<EfCoreSchemaProvider>(provider);
    }

    [Fact]
    public async Task UseSchemaFromEF_DiBuilder_SchemaContainsEfModelTables()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateInMemoryContext());
        services.AddSqlChat()
            .UseSchemaFromEF<TestDbContext>()
            .UseGenerator(new FakeSqlGenerator("SELECT 1"));

        using var scope = services.BuildServiceProvider().CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ISchemaProvider>();
        var catalog = await provider.GetSchemaAsync();

        Assert.Single(catalog.Tables, t => t.Name == "Blogs");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed class CapturingGenerator : ISqlGenerator
    {
        internal SchemaCatalog? LastSchema { get; private set; }

        public Task<string> GenerateAsync(string naturalLanguage, SchemaCatalog schema, CancellationToken ct = default)
        {
            LastSchema = schema;
            return Task.FromResult("SELECT 1");
        }
    }

    [Fact]
    public async Task UseSchemaFromEF_WithEnrichmentAndScopeValidation_ResolvesWithinScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSqlChat()
            .UseSchemaFromEF<TestDbContext>()
            .UseGenerator(new FakeSqlGenerator("SELECT 1"))
            .Enrich(e => e.ExcludeTable("Blogs"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var schemaProvider = scope.ServiceProvider.GetRequiredService<ISchemaProvider>();
        SchemaCatalog schema = await schemaProvider.GetSchemaAsync();

        Assert.IsType<EnrichingSchemaProvider>(schemaProvider);
        Assert.Empty(schema.Tables);
    }
}
