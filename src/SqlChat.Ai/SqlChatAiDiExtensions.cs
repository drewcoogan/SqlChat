using Microsoft.Extensions.DependencyInjection;
using SqlChat.Ai.Generators;
using SqlChat.Ai.Options;
using SqlChat.Client;
using SqlChat.Domain.Abstractions;

namespace SqlChat.Ai;

public static class SqlChatAiDiExtensions
{
    public static SqlChatDiBuilder UseAnthropic(this SqlChatDiBuilder builder, Action<SqlChatAiOptions>? configure = null)
    {
        var options = new SqlChatAiOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton<ISqlGenerator>(new AnthropicSqlGenerator(options));
        return builder;
    }
}
