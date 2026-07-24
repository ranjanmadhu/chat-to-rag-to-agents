using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Chatbot.Infrastructure.Ollama;

public sealed class OllamaModelAdminClient(HttpClient httpClient) : IOllamaModelAdminClient
{
    public async Task<IReadOnlyCollection<OllamaInstalledModel>> ListInstalledModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken);
        var models = payload?.Models?
            .Select(model =>
            {
                var name = model.Name?.Trim();
                var capabilities = model.Capabilities?
                    .Where(capability => !string.IsNullOrWhiteSpace(capability))
                    .Select(capability => capability.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? [];

                var supportsImage = capabilities.Contains("vision", StringComparer.OrdinalIgnoreCase);
                var supportsText = capabilities.Contains("completion", StringComparer.OrdinalIgnoreCase)
                    || capabilities.Contains("tools", StringComparer.OrdinalIgnoreCase)
                    || capabilities.Contains("thinking", StringComparer.OrdinalIgnoreCase);

                return string.IsNullOrWhiteSpace(name)
                    ? null
                    : new OllamaInstalledModel(name, capabilities, supportsText, supportsImage);
            })
            .Where(model => model is not null)
            .Select(model => model!)
            .GroupBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return models ?? [];
    }

    public async Task<OllamaWarmupResult> WarmupAsync(string model, CancellationToken cancellationToken)
    {
        var normalizedModel = model.Trim();
        var request = new OllamaWarmupRequest(
            normalizedModel,
            [new OllamaMessage("user", "ping")],
            Stream: false,
            KeepAlive: "10m",
            Options: new OllamaWarmupOptions(NumPredict: 1));

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new OllamaWarmupResult(true, normalizedModel);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new OllamaWarmupResult(
                    false,
                    normalizedModel,
                    $"Model '{normalizedModel}' is not installed. Run: ollama pull {normalizedModel}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var trimmed = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
            return new OllamaWarmupResult(false, normalizedModel, trimmed ?? $"Warmup failed with status {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OllamaWarmupResult(false, normalizedModel, "Warmup timed out while loading the model.");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return new OllamaWarmupResult(false, normalizedModel, "Ollama is unavailable. Ensure ollama serve is running.");
        }
    }

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyCollection<OllamaTagModel>? Models);

    private sealed record OllamaTagModel(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("capabilities")] IReadOnlyCollection<string>? Capabilities);

    private sealed record OllamaWarmupRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyCollection<OllamaMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        [property: JsonPropertyName("options")] OllamaWarmupOptions Options);

    private sealed record OllamaMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaWarmupOptions(
        [property: JsonPropertyName("num_predict")] int NumPredict);
}
