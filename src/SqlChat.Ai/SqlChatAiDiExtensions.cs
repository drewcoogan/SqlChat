using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlChat.Ai.Generators;
using SqlChat.Ai.Options;
using SqlChat.Client;
using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Options;

namespace SqlChat.Ai;

public static class SqlChatAiDiExtensions
{
    public static SqlChatDiBuilder UseAnthropic(this SqlChatDiBuilder builder, Action<SqlChatOptions>? configure = null)
    {
        var options = new SqlChatOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton<ISqlGenerator>(new AnthropicSqlGenerator(options));

        // Makes QueryRetryMax / LlmFixRetryMax set here take effect. WithOptions() still wins when both are used.
        builder.Services.TryAddSingleton(options);
        return builder;
    }

    public static SqlChatDiBuilder UseOpenAi(this SqlChatDiBuilder builder, Action<OpenAiSqlGeneratorOptions>? configure = null)
    {
        var options = new OpenAiSqlGeneratorOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton<ISqlGenerator>(new OpenAiSqlGenerator(options));
        return builder;
    }
}
