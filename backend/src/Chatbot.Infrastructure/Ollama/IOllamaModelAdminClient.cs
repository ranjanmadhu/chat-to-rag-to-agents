namespace Chatbot.Infrastructure.Ollama;

public interface IOllamaModelAdminClient
{
    Task<IReadOnlyCollection<OllamaInstalledModel>> ListInstalledModelsAsync(CancellationToken cancellationToken);
    Task<OllamaWarmupResult> WarmupAsync(string model, CancellationToken cancellationToken);
}

public sealed record OllamaInstalledModel(
    string Name,
    IReadOnlyCollection<string> Capabilities,
    bool SupportsText,
    bool SupportsImage);

public sealed record OllamaWarmupResult(bool IsReady, string Model, string? Error = null);
