using Microsoft.EntityFrameworkCore.Metadata;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;
using SqlChat.Domain.Providers;
using SqlChat.Service.Engine;
using SqlChat.Service.Security;

namespace SqlChat.Client;

/// <summary>
/// Wires schema sources and builds a <see cref="SqlChatClient"/>.
/// </summary>
public sealed class SqlChatBuilder
{
    private ISchemaProvider? _schemaProvider;
    private ISqlSecurityValidator? _sqlSecurityValidator;
    private SqlSecurityOptions? _sqlSecurityOptions;
    private ISqlGenerator? _generator;

    public SqlChatBuilder UseSchema(SchemaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _schemaProvider = new ManualSchemaProvider(catalog);
        return this;
    }

    /// <summary>
    /// Use the metadata from an EF Core model (typically <c>dbContext.Model</c>).
    /// </summary>
    public SqlChatBuilder UseEntityFramework(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _schemaProvider = new EfCoreSchemaProvider(model);
        return this;
    }

    /// <summary>
    /// Plug in an LLM-backed or custom <see cref="ISqlGenerator"/> (e.g. AnthropicSqlGenerator).
    /// Defaults to <see cref="SimpleTextToSqlGenerator"/> when not set.
    /// </summary>
    public SqlChatBuilder UseGenerator(ISqlGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        return this;
    }

    /// <summary>
    /// Advanced: supply a custom <see cref="ISchemaProvider"/> (tests, caching, composite sources, etc.).
    /// </summary>
    public SqlChatBuilder UseSchemaProvider(ISchemaProvider provider)
    {
        _schemaProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Replace the default <see cref="SqlSecurityValidator"/> (e.g. custom rules or tests).
    /// </summary>
    public SqlChatBuilder UseSqlSecurity(ISqlSecurityValidator validator)
    {
        _sqlSecurityValidator = validator ?? throw new ArgumentNullException(nameof(validator));
        _sqlSecurityOptions = null;
        return this;
    }

    /// <summary>
    /// Configure the built-in <see cref="SqlSecurityValidator"/> (heuristic checks; optional T-SQL AST).
    /// Ignored if <see cref="UseSqlSecurity(ISqlSecurityValidator)"/> was called with a custom validator.
    /// </summary>
    public SqlChatBuilder UseSqlSecurity(SqlSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sqlSecurityValidator = null;
        _sqlSecurityOptions = options;
        return this;
    }

    public SqlChatClient Build()
    {
        if (_schemaProvider is null)
            throw new InvalidOperationException(
                "Configure a schema source with UseSchema, UseEntityFramework, or UseSchemaProvider before calling Build().");

        if (_generator is null)
            throw new InvalidOperationException(
                "Configure a SQL generator with UseGenerator before calling Build(). Example: .UseGenerator(new AnthropicSqlGenerator(options))");

        var sqlSecurity = _sqlSecurityValidator ?? new SqlSecurityValidator(_sqlSecurityOptions);
        var engine = new TextToSqlEngine(_schemaProvider, _generator, sqlSecurity);
        return new SqlChatClient(engine);
    }
}
