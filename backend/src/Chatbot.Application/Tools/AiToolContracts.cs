namespace Chatbot.Application.Tools;

public sealed record AiToolParameterDefinition(
    string Name,
    string Type,
    string Description,
    bool IsRequired);

public sealed record AiToolDefinition(
    string Id,
    string Description,
    IReadOnlyCollection<AiToolParameterDefinition> Parameters);

public sealed record AiToolCall(
    string ToolId,
    string ArgumentsJson,
    string? CallId = null);
