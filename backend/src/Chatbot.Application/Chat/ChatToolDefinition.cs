namespace Chatbot.Application.Chat;

public sealed record ChatToolDefinition(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyCollection<string> UsageHints,
    string Category = "General",
    bool IsDeterministic = true);

public sealed record ChatToolExecutionResult(
    string ToolId,
    string Output);

public interface IChatToolService
{
    IReadOnlyCollection<ChatToolDefinition> GetAvailableTools();

    Task<ChatToolExecutionResult?> TryExecuteAsync(
        string message,
        IReadOnlyCollection<string>? enabledToolIds,
        CancellationToken cancellationToken);
}
