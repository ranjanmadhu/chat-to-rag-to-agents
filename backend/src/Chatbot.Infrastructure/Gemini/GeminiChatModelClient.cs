using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chatbot.Application.Chat;
using Chatbot.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Gemini;

public sealed class GeminiChatModelClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiChatModelClient> logger) : IChatModelClient
{
    private readonly GeminiOptions _options = options.Value;

    public async Task<ChatModelResponse> SendAsync(IReadOnlyCollection<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini API key was not configured.");
            return new ChatModelResponse(new ChatMessage(
                "assistant",
                "Gemini API key is not configured. Set GEMINI_API_KEY (or Gemini__ApiKey) before using this provider."));
        }

        try
        {
            var request = new GeminiGenerateContentRequest(
                messages.Select(message => new GeminiContent(
                    ResolveRole(message.Role),
                    new[] { new GeminiPart(message.Content) })).ToArray());

            var model = ResolveModel();
            var requestUri = $"/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.PostAsJsonAsync(requestUri, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<GeminiGenerateResponse>(cancellationToken);
            stopwatch.Stop();
            var text = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

            return new ChatModelResponse(
                new ChatMessage("assistant", text),
                BuildMetrics(body?.UsageMetadata, stopwatch.Elapsed));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            logger.LogWarning(ex, "Gemini request failed with status {StatusCode}.", ex.StatusCode);
            return new ChatModelResponse(new ChatMessage(
                "assistant",
                $"Gemini request failed ({ex.StatusCode}). Check that the API key is valid and that the selected model is available for your account."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini request failed.");
            return new ChatModelResponse(new ChatMessage(
                "assistant",
                "Gemini is currently unavailable. Check the configured API key and model settings."));
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        IReadOnlyCollection<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini API key was not configured for streaming.");
            yield return new ChatStreamChunk("Gemini API key is not configured. Set GEMINI_API_KEY (or Gemini__ApiKey) before using this provider.");
            yield return new ChatStreamChunk(string.Empty, IsDone: true);
            yield break;
        }

        var request = new GeminiGenerateContentRequest(
            messages.Select(message => new GeminiContent(
                ResolveRole(message.Role),
                new[] { new GeminiPart(message.Content) })).ToArray());

        await foreach (var chunk in StreamFromGeminiAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<ChatStreamChunk> StreamFromGeminiAsync(
        GeminiGenerateContentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = ResolveModel();
        var apiKey = ResolveApiKey();
        var requestUri = $"/v1beta/models/{model}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(request)
        };

        HttpResponseMessage? response = null;
        string? fallbackContent = null;
        try
        {
            response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            logger.LogWarning(ex, "Gemini streaming request failed with status {StatusCode}.", ex.StatusCode);
            fallbackContent = $"Gemini request failed ({ex.StatusCode}). Check that the API key is valid and that the selected model is available for your account.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Gemini stream is unavailable.");
            fallbackContent = "Gemini is currently unavailable. Check the configured API key and model settings.";
        }

        if (fallbackContent is not null)
        {
            response?.Dispose();
            yield return new ChatStreamChunk(fallbackContent);
            yield return new ChatStreamChunk(string.Empty, IsDone: true);
            yield break;
        }

        var successfulResponse = response ?? throw new InvalidOperationException("Gemini stream response was not initialized.");
        var stopwatch = Stopwatch.StartNew();
        GeminiUsageMetadata? usageMetadata = null;

        using (successfulResponse)
        {
            await using var contentStream = await successfulResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(contentStream);

            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                var payloadLine = NormalizeStreamPayloadLine(line);
                if (payloadLine is null)
                {
                    continue;
                }

                var payload = TryDeserializeStreamPayload(payloadLine);
                usageMetadata = payload?.UsageMetadata ?? usageMetadata;
                var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return new ChatStreamChunk(text);
                }
            }
        }

        stopwatch.Stop();
        yield return new ChatStreamChunk(string.Empty, IsDone: true, Metrics: BuildMetrics(usageMetadata, stopwatch.Elapsed));
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return _options.ApiKey;
        }

        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
            Environment.GetEnvironmentVariable("GOOGLE_API_KEY"),
            Environment.GetEnvironmentVariable("Gemini__ApiKey"),
            Environment.GetEnvironmentVariable("Gemini__Api_Key"),
            Environment.GetEnvironmentVariable("Gemini__apikey"));
    }

    private string ResolveModel()
    {
        return FirstNonEmpty(
            _options.Model,
            Environment.GetEnvironmentVariable("GEMINI_MODEL"),
            Environment.GetEnvironmentVariable("GOOGLE_GEMINI_MODEL"),
            Environment.GetEnvironmentVariable("Gemini__Model"))
            ?? "gemini-2.0-flash";
    }

    private static string ResolveRole(string role) => role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";

    private static GeminiStreamChunkResponse? TryDeserializeStreamPayload(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<GeminiStreamChunkResponse>(line, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeStreamPayloadLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["data:".Length..].Trim();
        }

        return trimmed.StartsWith("{", StringComparison.Ordinal) ? trimmed : null;
    }

    private static ChatMetrics? BuildMetrics(GeminiUsageMetadata? usageMetadata, TimeSpan elapsed)
    {
        if (usageMetadata is null)
        {
            return null;
        }

        double? outputTokensPerSecond = null;
        if (usageMetadata.CandidatesTokenCount is > 0 && elapsed.TotalSeconds > 0)
        {
            outputTokensPerSecond = usageMetadata.CandidatesTokenCount.Value / elapsed.TotalSeconds;
        }

        return new ChatMetrics(
            usageMetadata.PromptTokenCount,
            usageMetadata.CandidatesTokenCount,
            outputTokensPerSecond);
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

        return string.Empty;
    }

    private sealed record GeminiGenerateContentRequest(
        [property: JsonPropertyName("contents")] IReadOnlyCollection<GeminiContent> Contents);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyCollection<GeminiPart> Parts);

    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string Text);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record GeminiGenerateResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyCollection<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadata? UsageMetadata);

    private sealed record GeminiStreamChunkResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyCollection<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadata? UsageMetadata);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiCandidateContent? Content);

    private sealed record GeminiCandidateContent(
        [property: JsonPropertyName("parts")] IReadOnlyCollection<GeminiTextPart>? Parts);

    private sealed record GeminiTextPart(
        [property: JsonPropertyName("text")] string? Text);

    private sealed record GeminiUsageMetadata(
        [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
        [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount,
        [property: JsonPropertyName("totalTokenCount")] int? TotalTokenCount);
}
