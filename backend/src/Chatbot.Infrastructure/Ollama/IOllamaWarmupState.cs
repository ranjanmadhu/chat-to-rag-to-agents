using System.Collections.Concurrent;

namespace Chatbot.Infrastructure.Ollama;

public interface IOllamaWarmupState
{
    void Begin(string model);
    void Complete(string model, bool success, string? error = null);
    OllamaWarmupStatus Get(string model);
}

public sealed class OllamaWarmupState : IOllamaWarmupState
{
    private readonly ConcurrentDictionary<string, OllamaWarmupStatus> _states = new(StringComparer.OrdinalIgnoreCase);

    public void Begin(string model)
    {
        var normalized = Normalize(model);
        _states[normalized] = new OllamaWarmupStatus(normalized, IsWarming: true, IsReady: false, Error: null, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public void Complete(string model, bool success, string? error = null)
    {
        var normalized = Normalize(model);
        _states[normalized] = new OllamaWarmupStatus(
            normalized,
            IsWarming: false,
            IsReady: success,
            Error: success ? null : error,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public OllamaWarmupStatus Get(string model)
    {
        var normalized = Normalize(model);
        if (_states.TryGetValue(normalized, out var status))
        {
            return status;
        }

        return new OllamaWarmupStatus(normalized, IsWarming: false, IsReady: false, Error: null, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string Normalize(string model)
    {
        return model.Trim();
    }
}

public sealed record OllamaWarmupStatus(
    string Model,
    bool IsWarming,
    bool IsReady,
    string? Error,
    DateTimeOffset UpdatedAtUtc);
