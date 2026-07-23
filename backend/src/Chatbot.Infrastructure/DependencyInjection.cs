using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Gemini;
using Chatbot.Infrastructure.Ollama;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.Configure<GeminiOptions>(configuration.GetSection("Gemini"));
        services.AddSingleton<IChatModelClientFactory, ChatModelClientFactory>();

        services.AddHttpClient<OllamaChatModelClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<OllamaOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds));
        });

        services.AddHttpClient<GeminiChatModelClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<GeminiOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds));
        });

        services.AddTransient<IChatModelClient>(serviceProvider =>
            serviceProvider.GetRequiredService<OllamaChatModelClient>());

        services.AddTransient<IChatModelClient>(serviceProvider =>
            serviceProvider.GetRequiredService<GeminiChatModelClient>());

        return services;
    }
}
