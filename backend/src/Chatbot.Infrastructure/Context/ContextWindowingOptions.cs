namespace Chatbot.Infrastructure.Context;

public sealed class ContextWindowingOptions
{
    public const string SectionName = "ContextWindowing";

    public double SafetyRatio { get; init; } = 0.8;
    public int ReservedOutputTokens { get; init; } = 2048;
    public int ApproxCharsPerToken { get; init; } = 4;
    public int DefaultMaxContextTokens { get; init; } = 32_768;
    public int GlobalMaxContextChars { get; init; } = 1_000_000;
    public int MinContextChars { get; init; } = 8_000;
    public IReadOnlyCollection<ModelContextLimit> ModelLimits { get; init; } = [];
}

public sealed class ModelContextLimit
{
    public string? Provider { get; init; }
    public string Model { get; init; } = string.Empty;
    public int MaxContextTokens { get; init; }
}
