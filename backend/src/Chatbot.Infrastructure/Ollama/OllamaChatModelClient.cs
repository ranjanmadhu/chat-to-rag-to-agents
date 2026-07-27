using System.Net.Http.Json;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chatbot.Application.Chat;
using Chatbot.Application.Tools;
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

    public async Task<ChatModelResponse> SendAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        IReadOnlyCollection<AiToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var resolvedModel = ResolveModel(model);
        var request = new OllamaChatRequest(
            resolvedModel,
            messages.Select(MapMessage).ToArray(),
            Stream: false,
            Tools: MapTools(tools));

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
            var toolCalls = body?.Message?.ToolCalls?
                .Where(call => !string.IsNullOrWhiteSpace(call.Function?.Name))
                .Select(call => new AiToolCall(
                    call.Function!.Name!,
                    call.Function.Arguments.ValueKind is JsonValueKind.Undefined
                        ? "{}"
                        : call.Function.Arguments.GetRawText(),
                    call.Id))
                .ToArray();

            return new ChatModelResponse(
                new ChatMessage("assistant", body?.Message?.Content ?? string.Empty),
                resolvedModel,
                BuildMetrics(body?.PromptEvalCount, body?.EvalCount, body?.EvalDuration),
                toolCalls);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(ex, "Configured Ollama model {Model} was not found.", resolvedModel);

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                $"The configured Ollama model '{resolvedModel}' was not found. Pull it with `ollama pull {resolvedModel}` or update appsettings to a model you already have."),
                resolvedModel);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama request timed out after {TimeoutSeconds}s.", _options.RequestTimeoutSeconds);

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                $"Ollama took too long to respond (timeout: {_options.RequestTimeoutSeconds}s). Try a smaller model, ask for a shorter answer, or increase Ollama:RequestTimeoutSeconds in appsettings."),
                resolvedModel);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Ollama is unavailable. Returning tutorial fallback response.");

            return new ChatModelResponse(new ChatMessage(
                "assistant",
                "Ollama is not reachable yet. Start it with `ollama serve`, pull the configured model, then ask again."),
                resolvedModel);
        }
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        IReadOnlyCollection<AiToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolvedModel = ResolveModel(model);
        var request = new OllamaChatRequest(
            resolvedModel,
            messages.Select(MapMessage).ToArray(),
            Stream: true,
            Tools: MapTools(tools));

        IAsyncEnumerable<ChatStreamChunk> stream;

        try
        {
            stream = StreamFromOllamaAsync(request, resolvedModel, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning(ex, "Configured Ollama model {Model} was not found.", resolvedModel);
            stream = SingleMessageAsync(
                $"The configured Ollama model '{resolvedModel}' was not found. Pull it with `ollama pull {resolvedModel}` or update appsettings to a model you already have.",
                resolvedModel);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Ollama stream timed out after {TimeoutSeconds}s.", _options.RequestTimeoutSeconds);
            stream = SingleMessageAsync(
                $"Ollama took too long to respond (timeout: {_options.RequestTimeoutSeconds}s). Try a smaller model, ask for a shorter answer, or increase Ollama:RequestTimeoutSeconds in appsettings.",
                resolvedModel);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Ollama stream is unavailable.");
            stream = SingleMessageAsync(
                "Ollama is not reachable yet. Start it with `ollama serve`, pull the configured model, then ask again.",
                resolvedModel);
        }

        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<ChatStreamChunk> StreamFromOllamaAsync(
        OllamaChatRequest request,
        string model,
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
                yield return new ChatStreamChunk(chunk.Message.Content, Model: model);
            }

            if (chunk?.Done == true)
            {
                yield return new ChatStreamChunk(
                    string.Empty,
                    IsDone: true,
                    Model: model,
                    Metrics: BuildMetrics(chunk.PromptEvalCount, chunk.EvalCount, chunk.EvalDuration));
                yield break;
            }
        }
    }

    private static async IAsyncEnumerable<ChatStreamChunk> SingleMessageAsync(string content, string model)
    {
        await Task.Yield();
        yield return new ChatStreamChunk(content, Model: model);
        yield return new ChatStreamChunk(string.Empty, IsDone: true, Model: model);
    }

    private string ResolveModel(string? requestModel)
    {
        return string.IsNullOrWhiteSpace(requestModel) ? _options.ChatModel : requestModel.Trim();
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

    private static OllamaMessage MapMessage(ChatMessage message)
    {
        var images = message.Images?.Select(image => image.Base64Data).ToArray();
        return new OllamaMessage(message.Role, message.Content, images);
    }

    private static IReadOnlyCollection<OllamaToolDefinition>? MapTools(IReadOnlyCollection<AiToolDefinition>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return null;
        }

        return tools
            .Select(tool => new OllamaToolDefinition(
                "function",
                new OllamaToolFunction(
                    tool.Id,
                    tool.Description,
                    new OllamaToolParameters(
                        "object",
                        tool.Parameters.ToDictionary(
                            parameter => parameter.Name,
                            parameter => new OllamaToolProperty(parameter.Type, parameter.Description)),
                        tool.Parameters.Where(parameter => parameter.IsRequired).Select(parameter => parameter.Name).ToArray()))))
            .ToArray();
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyCollection<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("tools")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyCollection<OllamaToolDefinition>? Tools = null);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("images")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyCollection<string>? Images = null,
        [property: JsonPropertyName("tool_calls")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyCollection<OllamaToolCall>? ToolCalls = null);

    private sealed record OllamaToolDefinition(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] OllamaToolFunction Function);

    private sealed record OllamaToolFunction(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] OllamaToolParameters Parameters);

    private sealed record OllamaToolParameters(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, OllamaToolProperty> Properties,
        [property: JsonPropertyName("required")] IReadOnlyCollection<string> Required);

    private sealed record OllamaToolProperty(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description);

    private sealed record OllamaToolCall(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("function")] OllamaToolCallFunction? Function);

    private sealed record OllamaToolCallFunction(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] JsonElement Arguments);

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
