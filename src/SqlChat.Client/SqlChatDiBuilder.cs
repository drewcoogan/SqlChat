using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlChat.Data.Execution;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Builders;
using SqlChat.Domain.Extensions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Options;
using SqlChat.Service.Engine;
using SqlChat.Service.Security;

namespace SqlChat.Client;

public sealed class SqlChatDiBuilder
{
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
    }

    public SqlChatDiBuilder UseEntityFramework<TContext>() where TContext : DbContext
    {
        Services.AddScoped(sp =>
        {
            var context = sp.GetRequiredService<TContext>();
            var cs = context.Database.GetConnectionString()
                ?? throw new InvalidOperationException($"{typeof(TContext).Name} has no connection string configured.");
            return new PostgresSchemaProvider(cs);
        });
        Services.AddScoped<ISchemaProvider>(sp => sp.GetRequiredService<PostgresSchemaProvider>());
        return this;
    }

    public SqlChatDiBuilder UsePostgresConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Services.AddSingleton(new PostgresSchemaProvider(connectionString));
        Services.AddSingleton<ISchemaProvider>(sp => sp.GetRequiredService<PostgresSchemaProvider>());
        Services.AddSingleton<IQueryExecutor>(sp =>
            new PostgresQueryExecutor(
                sp.GetService<SqlChatOptions>() ?? new SqlChatOptions { ConnectionString = connectionString }));
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
        configure(options);
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
        Services.AddSingleton(enrichment.Validate());
        Services.RemoveAll<ISchemaProvider>();
        Services.AddSingleton<ISchemaProvider>(sp =>
            new EnrichingSchemaProvider(
                sp.GetRequiredService<PostgresSchemaProvider>(),
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

}
