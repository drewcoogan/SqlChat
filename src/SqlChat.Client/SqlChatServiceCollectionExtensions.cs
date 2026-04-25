using Microsoft.Extensions.DependencyInjection;

namespace SqlChat.Client;

public static class SqlChatServiceCollectionExtensions
{
    public static SqlChatDiBuilder AddSqlChat(this IServiceCollection services) =>
        new(services);
}
