using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Context;
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
        services.Configure<ContextWindowingOptions>(configuration.GetSection(ContextWindowingOptions.SectionName));
        services.AddSingleton<IChatModelClientFactory, ChatModelClientFactory>();
        services.AddSingleton<IOllamaWarmupState, OllamaWarmupState>();
        services.AddSingleton<IContextWindowBudgetResolver, ContextWindowBudgetResolver>();
        services.AddSingleton<IContextTokenCounter, ProviderContextTokenCounter>();

        services.AddHttpClient<OllamaChatModelClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<OllamaOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds));
        });

        services.AddHttpClient<IOllamaModelAdminClient, OllamaModelAdminClient>((serviceProvider, httpClient) =>
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

        services.AddHttpClient("Context.GeminiTokenCounter", (serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<GeminiOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds));
        });

        services.AddHttpClient("Context.OllamaTokenCounter", (serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<OllamaOptions>>()
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
