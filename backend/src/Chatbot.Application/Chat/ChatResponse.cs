namespace Chatbot.Application.Chat;

public sealed record ToolObservability(
    string? DecisionSource,
    string? Summary,
    IReadOnlyCollection<string>? Steps = null,
    string? NotUsedReason = null,
    string? UsedToolId = null,
    IReadOnlyCollection<string>? EnabledToolIds = null);

public sealed record ChatResponse(
	string Message,
	string Model,
	ChatMetrics? Metrics = null,
	ToolObservability? Observability = null);
