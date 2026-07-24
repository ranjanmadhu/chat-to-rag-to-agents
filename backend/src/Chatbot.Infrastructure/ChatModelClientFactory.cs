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

        var enabledProviders = ParseEnabledProviders(
            configuration["EnabledProviders"],
            configuration["ENABLED_PROVIDERS"]);

        if (enabledProviders.Count > 0 && !enabledProviders.Contains(normalized))
        {
            throw new InvalidOperationException(
                $"Provider '{normalized}' is not enabled. Enabled providers: {string.Join(", ", enabledProviders.OrderBy(p => p))}.");
        }

        return normalized switch
        {
            "gemini" => clients.FirstOrDefault(client => client is Gemini.GeminiChatModelClient)
                ?? throw new InvalidOperationException("Gemini chat client is not registered."),
            "ollama" => clients.FirstOrDefault(client => client is Ollama.OllamaChatModelClient)
                ?? throw new InvalidOperationException("Ollama chat client is not registered."),
            _ => throw new InvalidOperationException($"Unsupported provider '{normalized}'. Supported providers: gemini, ollama.")
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

        return "gemini";
    }

    private static HashSet<string> ParseEnabledProviders(params string?[] values)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var segment in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                providers.Add(segment.ToLowerInvariant());
            }
        }

        return providers;
    }
}
