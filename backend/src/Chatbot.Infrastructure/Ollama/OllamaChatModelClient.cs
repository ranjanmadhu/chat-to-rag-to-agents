using System.Net.Http.Json;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chatbot.Application.Chat;
using Chatbot.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Ollama;

public sealed class OllamaChatModelClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaChatModelClient> logger) : IChatModelClient
{
    private const double NanosecondsPerSecond = 1_000_000_000d;
    private readonly OllamaOptions _options = options.Value;

    public async Task<ChatModelResponse> SendAsync(IReadOnlyCollection<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            _options.ChatModel,
            messages.Select(message => new OllamaMessage(message.Role, message.Content)).ToArray(),
            Stream: false);

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
            return new ChatModelResponse(
                new ChatMessage("assistant", body?.Message?.Content ?? string.Empty),
                BuildMetrics(body?.PromptEvalCount, body?.EvalCount, body?.EvalDuration));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(ex, "Configured Ollama model {Model} was not found.", _options.ChatModel);

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                $"The configured Ollama model '{_options.ChatModel}' was not found. Pull it with `ollama pull {_options.ChatModel}` or update appsettings to a model you already have."));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama request timed out after {TimeoutSeconds}s.", _options.RequestTimeoutSeconds);

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                $"Ollama took too long to respond (timeout: {_options.RequestTimeoutSeconds}s). Try a smaller model, ask for a shorter answer, or increase Ollama:RequestTimeoutSeconds in appsettings."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Ollama is unavailable. Returning tutorial fallback response.");

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                "Ollama is not reachable yet. Start it with `ollama serve`, pull the configured model, then ask again."));
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        IReadOnlyCollection<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            _options.ChatModel,
            messages.Select(message => new OllamaMessage(message.Role, message.Content)).ToArray(),
            Stream: true);

        IAsyncEnumerable<ChatStreamChunk> stream;

        try
        {
            stream = StreamFromOllamaAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(ex, "Configured Ollama model {Model} was not found.", _options.ChatModel);
            stream = SingleMessageAsync(
                $"The configured Ollama model '{_options.ChatModel}' was not found. Pull it with `ollama pull {_options.ChatModel}` or update appsettings to a model you already have.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama stream timed out after {TimeoutSeconds}s.", _options.RequestTimeoutSeconds);
            stream = SingleMessageAsync(
                $"Ollama took too long to respond (timeout: {_options.RequestTimeoutSeconds}s). Try a smaller model, ask for a shorter answer, or increase Ollama:RequestTimeoutSeconds in appsettings.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Ollama stream is unavailable.");
            stream = SingleMessageAsync(
                "Ollama is not reachable yet. Start it with `ollama serve`, pull the configured model, then ask again.");
        }

        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<ChatStreamChunk> StreamFromOllamaAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(contentStream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<OllamaStreamChatResponse>(line);
            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
            {
                yield return new ChatStreamChunk(chunk.Message.Content);
            }

            if (chunk?.Done == true)
            {
                yield return new ChatStreamChunk(
                    string.Empty,
                    IsDone: true,
                    Metrics: BuildMetrics(chunk.PromptEvalCount, chunk.EvalCount, chunk.EvalDuration));
                yield break;
            }
        }
    }

    private static async IAsyncEnumerable<ChatStreamChunk> SingleMessageAsync(string content)
    {
        await Task.Yield();
        yield return new ChatStreamChunk(content);
    }

    private static ChatMetrics? BuildMetrics(int? inputTokens, int? outputTokens, long? evalDurationNs)
    {
        double? tokensPerSecond = null;
        if (outputTokens is > 0 && evalDurationNs is > 0)
        {
            tokensPerSecond = outputTokens.Value / (evalDurationNs.Value / NanosecondsPerSecond);
        }

        if (inputTokens is null && outputTokens is null && tokensPerSecond is null)
        {
            return null;
        }

        return new ChatMetrics(inputTokens, outputTokens, tokensPerSecond);
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyCollection<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount,
        [property: JsonPropertyName("eval_duration")] long? EvalDuration);

    private sealed record OllamaStreamChatResponse(
        [property: JsonPropertyName("message")] OllamaMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount,
        [property: JsonPropertyName("eval_duration")] long? EvalDuration);
}
