using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Models;
using SqlChat.Service.Engine;
using SqlChat.Service.Generation;
using SqlChat.Service.Security;

namespace SqlChat.Client;

/// <summary>
/// Fluent builder returned by <c>services.AddSqlChat()</c>.
/// Register schema source and optionally override security/generator.
/// AI generators are registered via extension methods in SqlChat.Ai.
/// </summary>
public sealed class SqlChatDiBuilder
{
    public IServiceCollection Services { get; }

    internal SqlChatDiBuilder(IServiceCollection services)
    {
        Services = services;
        services.AddSingleton<ISqlGenerator, SimpleTextToSqlGenerator>();
        services.AddSingleton<ISqlSecurityValidator, SqlSecurityValidator>();
        services.AddScoped<TextToSqlEngine>();
        services.AddScoped<SqlChatClient>();
    }

    public SqlChatDiBuilder UseEntityFramework<TContext>() where TContext : DbContext
    {
        Services.AddScoped<ISchemaProvider>(sp =>
            new EfCoreSchemaProvider(sp.GetRequiredService<TContext>().Model));
        return this;
    }

    public SqlChatDiBuilder UseConnectionString(string connectionString, SqlDialect dialect)
    {
        ISchemaProvider provider = dialect switch
        {
            SqlDialect.SqlServer => new SqlServerSchemaProvider(connectionString),
            SqlDialect.PostgreSql => new PostgresSchemaProvider(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
        Services.AddSingleton(provider);
        return this;
    }

    public SqlChatDiBuilder UseGenerator(ISqlGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        Services.AddSingleton(generator);
        return this;
    }

    public SqlChatDiBuilder UseSqlSecurity(SqlSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Services.AddSingleton(options);
        Services.AddSingleton<ISqlSecurityValidator>(sp =>
            new SqlSecurityValidator(sp.GetRequiredService<SqlSecurityOptions>()));
        return this;
    }
}
