using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

public sealed class SqlChatBuilder
{
    private ISchemaProvider? _schemaProvider;
    private IQueryExecutor? _executor;
    private ISqlSecurityValidator? _sqlSecurityValidator;
    private SqlSecurityOptions? _sqlSecurityOptions;
    private ISqlGenerator? _generator;
    private int _llmFixRetryMax = 2;
    private SchemaEnrichment? _enrichment;

    public SqlChatBuilder UseConnectionString(string connectionString, int queryRetryMax = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new SqlChatOptions { ConnectionString = connectionString, QueryRetryMax = queryRetryMax };
        _schemaProvider = new PostgresSchemaProvider(connectionString);
        _executor = new PostgresQueryExecutor(options);
        return this;
    }

    public SqlChatBuilder UseSqlServerConnectionString(string connectionString, int queryRetryMax = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new SqlChatOptions { ConnectionString = connectionString, QueryRetryMax = queryRetryMax, Dialect = SqlDialect.SqlServer };
        _schemaProvider = new SqlServerSchemaProvider(connectionString);
        _executor = new SqlServerQueryExecutor(options);
        return this;
    }

    public SqlChatBuilder UseEntityFramework(DbContext context)
    {
        var cs = context.Validate().Database.GetConnectionString()
            ?? throw new InvalidOperationException("DbContext has no connection string configured.");
        _schemaProvider = new PostgresSchemaProvider(cs);
        return this;
    }

    public SqlChatBuilder UseSchema(SchemaCatalog catalog)
    {
        _schemaProvider = new ManualSchemaProvider(catalog.Validate());
        return this;
    }

    public SqlChatBuilder UseSchemaFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var catalog = SchemaCatalogSerializer.FromYaml(File.ReadAllText(path));
        _schemaProvider = new ManualSchemaProvider(catalog);
        return this;
    }

    public SqlChatBuilder UseSchemaProvider(ISchemaProvider provider)
    {
        _schemaProvider = provider.Validate();
        return this;
    }

    public SqlChatBuilder UseGenerator(ISqlGenerator generator)
    {
        _generator = generator.Validate();
        return this;
    }

    public SqlChatBuilder UseSqlSecurity(ISqlSecurityValidator validator)
    {
        _sqlSecurityValidator = validator.Validate();
        _sqlSecurityOptions = null;
        return this;
    }

    public SqlChatBuilder UseSqlSecurity(SqlSecurityOptions options)
    {
        _sqlSecurityValidator = null;
        _sqlSecurityOptions = options.Validate();
        return this;
    }

    public SqlChatBuilder WithLlmFixRetryMax(int max)
    {
        _llmFixRetryMax = max;
        return this;
    }

    public SqlChatBuilder Enrich(Action<SchemaEnrichmentBuilder> configure)
    {
        var builder = new SchemaEnrichmentBuilder();
        configure.Validate()(builder);
        _enrichment = builder.Build();
        return this;
    }

    public SqlChatBuilder Enrich(SchemaEnrichment enrichment)
    {
        _enrichment = enrichment.Validate();
        return this;
    }

    public SqlChatBuilder EnrichFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        _enrichment = JsonSerializer.Deserialize<SchemaEnrichment>(json, GetJsonSerializerOptions())
            ?? throw new InvalidOperationException($"Failed to deserialize enrichment file: {path}");
        return this;
    }

    public SqlChatClient Build()
    {
        if (_schemaProvider is null)
            throw new InvalidOperationException(
                "Configure a schema source with UseConnectionString, UseSchema, UseEntityFramework, or UseSchemaProvider before calling Build().");

        if (_generator is null)
            throw new InvalidOperationException(
                "Configure a SQL generator with UseGenerator before calling Build().");

        var schemaProvider = _enrichment is null
            ? _schemaProvider
            : new EnrichingSchemaProvider(_schemaProvider, _enrichment);

        var sqlSecurity = _sqlSecurityValidator ?? new SqlSecurityValidator(_sqlSecurityOptions);
        var engine = new TextToSqlEngine(schemaProvider, _generator, sqlSecurity);
        return new SqlChatClient(engine, _executor, _llmFixRetryMax);
    }

    private static JsonSerializerOptions GetJsonSerializerOptions() => new() { PropertyNameCaseInsensitive = true };
}
