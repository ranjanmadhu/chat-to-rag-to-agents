using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Ollama;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatbot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));

        services.AddHttpClient<IChatModelClient, OllamaChatModelClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(10, options.RequestTimeoutSeconds));
        });

        return services;
    }
}
