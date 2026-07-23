using Chatbot.Application.Chat;
using Microsoft.Extensions.Configuration;

namespace Chatbot.Infrastructure;

public sealed class ChatModelClientFactory(
    IEnumerable<IChatModelClient> clients,
    IConfiguration configuration) : IChatModelClientFactory
{
    public IChatModelClient Resolve(string? provider)
    {
        var normalized = FirstNonEmpty(
                provider,
                configuration["LLMProvider"],
                configuration["LLM_PROVIDER"])
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            "gemini" => clients.FirstOrDefault(client => client is Gemini.GeminiChatModelClient)
                ?? throw new InvalidOperationException("Gemini chat client is not registered."),
            _ => clients.FirstOrDefault(client => client is Ollama.OllamaChatModelClient)
                ?? clients.First()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "ollama";
    }
}
