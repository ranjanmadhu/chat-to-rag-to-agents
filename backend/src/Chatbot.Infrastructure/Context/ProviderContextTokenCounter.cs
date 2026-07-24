using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Gemini;
using Chatbot.Infrastructure.Ollama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Context;

public sealed class ProviderContextTokenCounter(
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<ProviderContextTokenCounter> logger) : IContextTokenCounter
{
    private readonly GeminiOptions _geminiOptions = geminiOptions.Value;
    private readonly OllamaOptions _ollamaOptions = ollamaOptions.Value;

    public async Task<int?> CountTextTokensAsync(string text, string? provider, string? model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (string.Equals(provider, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            return await CountGeminiTokensAsync(text, model, cancellationToken);
        }

        if (string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return await CountOllamaTokensAsync(text, model, cancellationToken);
        }

        var preferred = await CountGeminiTokensAsync(text, model, cancellationToken);
        if (preferred.HasValue)
        {
            return preferred;
        }

        return await CountOllamaTokensAsync(text, model, cancellationToken);
    }

    private async Task<int?> CountGeminiTokensAsync(string text, string? model, CancellationToken cancellationToken)
    {
        var apiKey = ResolveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("Context.GeminiTokenCounter");
            var resolvedModel = ResolveGeminiModel(model);
            var uri = $"/v1beta/models/{resolvedModel}:countTokens?key={Uri.EscapeDataString(apiKey)}";
            var request = new GeminiCountTokensRequest([
                new GeminiContent("user", [new GeminiPart(text)])
            ]);

            using var response = await client.PostAsJsonAsync(uri, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<GeminiCountTokensResponse>(cancellationToken);
            return body?.TotalTokens;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Gemini token counting is unavailable.");
            return null;
        }
    }

    private async Task<int?> CountOllamaTokensAsync(string text, string? model, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Context.OllamaTokenCounter");
            var resolvedModel = ResolveOllamaModel(model);
            var request = new OllamaTokenizeRequest(resolvedModel, text);

            using var response = await client.PostAsJsonAsync("/api/tokenize", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<OllamaTokenizeResponse>(cancellationToken);
            return body?.Tokens?.Count;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Ollama token counting is unavailable.");
            return null;
        }
    }

    private string ResolveGeminiApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_geminiOptions.ApiKey))
        {
            return _geminiOptions.ApiKey;
        }

        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
            ?? string.Empty;
    }

    private string ResolveGeminiModel(string? model)
    {
        return FirstNonEmpty(
            model,
            _geminiOptions.Model,
            Environment.GetEnvironmentVariable("GEMINI_MODEL"),
            Environment.GetEnvironmentVariable("GOOGLE_GEMINI_MODEL"),
            Environment.GetEnvironmentVariable("Gemini__Model"))
            ?? "gemini-3.6-flash";
    }

    private string ResolveOllamaModel(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? _ollamaOptions.ChatModel : model.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private sealed record GeminiCountTokensRequest(
        [property: JsonPropertyName("contents")] IReadOnlyCollection<GeminiContent> Contents);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyCollection<GeminiPart> Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string Text);

    private sealed record GeminiCountTokensResponse(
        [property: JsonPropertyName("totalTokens")] int? TotalTokens);

    private sealed record OllamaTokenizeRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaTokenizeResponse(
        [property: JsonPropertyName("tokens")] IReadOnlyCollection<int>? Tokens);
}
