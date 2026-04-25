using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SqlChat.Data.Providers;
using SqlChat.Domain.Abstractions;
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
        services.AddScoped<SqlChatClient>();
    }

    public SqlChatDiBuilder UseEntityFramework<TContext>() where TContext : DbContext
    {
        Services.AddScoped<ISchemaProvider>(sp =>
            new EfCoreSchemaProvider(sp.GetRequiredService<TContext>().Model));
        return this;
    }

    public SqlChatDiBuilder UsePostgresConnectionString(string connectionString)
    {
        Services.AddSingleton<ISchemaProvider>(new PostgresSchemaProvider(connectionString));
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
