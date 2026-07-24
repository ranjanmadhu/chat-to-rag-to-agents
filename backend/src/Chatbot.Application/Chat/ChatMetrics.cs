namespace Chatbot.Application.Chat;

public sealed record ChatMetrics(
    int? InputTokens,
    int? OutputTokens,
    double? OutputTokensPerSecond);

public sealed record ChatModelResponse(
    Chatbot.Domain.ChatMessage Message,
    string Model,
    ChatMetrics? Metrics = null);

public sealed record ChatStreamChunk(
    string Content,
    bool IsDone = false,
    string? Model = null,
    ChatMetrics? Metrics = null);