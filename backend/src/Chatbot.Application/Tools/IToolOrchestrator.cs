using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools;

public interface IToolOrchestrator
{
    IReadOnlyCollection<ChatToolDefinition> GetAvailableTools();

    Task<ChatToolExecutionResult?> TryExecuteAsync(
        string message,
        IReadOnlyCollection<string>? enabledToolIds,
        CancellationToken cancellationToken);
}
