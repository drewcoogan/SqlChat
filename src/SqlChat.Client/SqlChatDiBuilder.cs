using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlChat.Data.Execution;
using SqlChat.Data.Providers;
using SqlChat.Data.Serialization;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;
using SqlChat.Domain.Providers;
using SqlChat.Service.Engine;
using SqlChat.Service.Security;

namespace SqlChat.Client;

public sealed class SqlChatDiBuilder
{
    private Type? _concreteSchemaProviderType;

    public IServiceCollection Services { get; }

    internal SqlChatDiBuilder(IServiceCollection services)
    {
        Services = services;
        services.AddSingleton<ISqlSecurityValidator, SqlSecurityValidator>();
        services.AddScoped<TextToSqlEngine>();
        services.AddScoped(sp => new SqlChatClient(
            sp.GetRequiredService<TextToSqlEngine>(),
            sp.GetService<IQueryExecutor>(),
            sp.GetService<SqlChatOptions>()?.LlmFixRetryMax ?? 2));
        services.AddScoped<ISqlChatClient>(sp => sp.GetRequiredService<SqlChatClient>());
    }

    public SqlChatDiBuilder UseEntityFramework<TContext>() where TContext : DbContext
    {
        Services.AddScoped(sp =>
        {
            var context = sp.GetRequiredService<TContext>();
            if (context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) is true)
                throw new InvalidOperationException(
                    $"UseEntityFramework only supports PostgreSQL. Use UseSqlServerConnectionString() for SQL Server contexts, or UseSchemaFromEF<{typeof(TContext).Name}>() to let SqlChat detect the provider automatically.");
            var cs = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no connection string configured.");
            return new PostgresSchemaProvider(cs);
        });
        Services.AddScoped<ISchemaProvider>(sp => sp.GetRequiredService<PostgresSchemaProvider>());
        Services.AddScoped<IQueryExecutor>(sp =>
        {
            var context = sp.GetRequiredService<TContext>();
            var cs = context.Database.GetConnectionString();
            if (cs is null) return null!;
            return new PostgresQueryExecutor(ExecutorOptions(sp, cs, SqlDialect.PostgreSql));
        });
        _concreteSchemaProviderType = typeof(PostgresSchemaProvider);
        return this;
    }

    public SqlChatDiBuilder UseSchemaFromEF<TContext>() where TContext : DbContext
    {
        Services.AddScoped(sp => new EfCoreSchemaProvider(sp.GetRequiredService<TContext>().Model));
        Services.AddScoped<ISchemaProvider>(sp => sp.GetRequiredService<EfCoreSchemaProvider>());
        Services.AddScoped<IQueryExecutor>(sp =>
        {
            var context = sp.GetRequiredService<TContext>();
            var cs = context.Database.IsRelational() ? context.Database.GetConnectionString() : null;

            // InMemory or no-connection contexts: return null so SqlChatClient receives no executor.
            // QueryAsync will be unavailable; GenerateSqlAsync works without a live DB.
            if (cs is null) return null!;

            return context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) is true
                ? new SqlServerQueryExecutor(ExecutorOptions(sp, cs, SqlDialect.SqlServer))
                : new PostgresQueryExecutor(ExecutorOptions(sp, cs, SqlDialect.PostgreSql));
        });
        _concreteSchemaProviderType = typeof(EfCoreSchemaProvider);
        return this;
    }

    public SqlChatDiBuilder UsePostgresConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Services.AddSingleton(new PostgresSchemaProvider(connectionString));
        Services.AddSingleton<ISchemaProvider>(sp => sp.GetRequiredService<PostgresSchemaProvider>());
        Services.AddSingleton<IQueryExecutor>(sp =>
            new PostgresQueryExecutor(ExecutorOptions(sp, connectionString, SqlDialect.PostgreSql)));
        _concreteSchemaProviderType = typeof(PostgresSchemaProvider);
        return this;
    }

    public SqlChatDiBuilder UseSqlServerConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Services.AddSingleton(new SqlServerSchemaProvider(connectionString));
        Services.AddSingleton<ISchemaProvider>(sp => sp.GetRequiredService<SqlServerSchemaProvider>());
        Services.AddSingleton<IQueryExecutor>(sp =>
            new SqlServerQueryExecutor(ExecutorOptions(sp, connectionString, SqlDialect.SqlServer)));
        _concreteSchemaProviderType = typeof(SqlServerSchemaProvider);
        return this;
    }

    public SqlChatDiBuilder UseSchemaFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var catalog = Path.GetExtension(path).Equals(".json", StringComparison.InvariantCultureIgnoreCase)
            ? SchemaCatalogSerializer.FromJson(File.ReadAllText(path))
            : SchemaCatalogSerializer.FromYaml(File.ReadAllText(path));
        Services.AddSingleton<ISchemaProvider>(new ManualSchemaProvider(catalog));
        return this;
    }

    public SqlChatDiBuilder UseGenerator(ISqlGenerator generator)
    {
        Services.AddSingleton(generator.Validate());
        return this;
    }

    public SqlChatDiBuilder UseSqlSecurity(SqlSecurityOptions options)
    {
        Services.AddSingleton(options.Validate());
        Services.AddSingleton<ISqlSecurityValidator>(sp =>
            new SqlSecurityValidator(sp.GetRequiredService<SqlSecurityOptions>()));
        return this;
    }

    public SqlChatDiBuilder WithOptions(Action<SqlChatOptions> configure)
    {
        var options = new SqlChatOptions();
        configure.Validate()(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.LlmFixRetryMax);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QueryRetryMax);
        Services.AddSingleton(options);
        return this;
    }

    public SqlChatDiBuilder Enrich(Action<SchemaEnrichmentBuilder> configure)
    {
        var builder = new SchemaEnrichmentBuilder();
        configure.Validate()(builder);
        return Enrich(builder.Build());
    }

    public SqlChatDiBuilder Enrich(SchemaEnrichment enrichment)
    {
        if (_concreteSchemaProviderType is null)
            throw new InvalidOperationException(
                "Call UsePostgresConnectionString or UseSqlServerConnectionString before Enrich.");

        Services.AddSingleton(enrichment.Validate());
        Services.RemoveAll<ISchemaProvider>();
        var providerType = _concreteSchemaProviderType;
        // Scoped, not singleton: the EF-backed providers are scoped (they hold a DbContext), and a singleton
        // must not capture a scoped service.
        Services.AddScoped<ISchemaProvider>(sp =>
            new EnrichingSchemaProvider(
                (ISchemaProvider)sp.GetRequiredService(providerType),
                sp.GetRequiredService<SchemaEnrichment>()));
        return this;
    }

    public SqlChatDiBuilder EnrichFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        var enrichment = JsonSerializer.Deserialize<SchemaEnrichment>(json, GetJsonSerializerOptions())
            ?? throw new InvalidOperationException($"Failed to deserialize enrichment file: {path}");
        return Enrich(enrichment);
    }

    private static JsonSerializerOptions GetJsonSerializerOptions() => new() { PropertyNameCaseInsensitive = true };

    // Options from WithOptions() supply the tuning values only. The connection string and dialect always come
    // from the Use*() call that chose the database, so the executor talks to the same DB as the schema provider.
    private static SqlChatOptions ExecutorOptions(IServiceProvider sp, string connectionString, SqlDialect dialect)
    {
        SqlChatOptions? configured = sp.GetService<SqlChatOptions>();
        SqlChatOptions options = new() { ConnectionString = connectionString, Dialect = dialect };
        if (configured is not null)
        {
            options.QueryRetryMax = configured.QueryRetryMax;
            options.LlmFixRetryMax = configured.LlmFixRetryMax;
        }

        return options;
    }
}
