namespace Chatbot.Application.Chat;

using Chatbot.Application.Tools;

public sealed record ChatMetrics(
    int? InputTokens,
    int? OutputTokens,
    double? OutputTokensPerSecond);

public sealed record ChatModelResponse(
    Chatbot.Domain.ChatMessage Message,
    string Model,
    ChatMetrics? Metrics = null,
    IReadOnlyCollection<AiToolCall>? ToolCalls = null);

public sealed record ChatStreamChunk(
    string Content,
    bool IsDone = false,
    string? Model = null,
    ChatMetrics? Metrics = null,
    ToolObservability? Observability = null);