using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools;

public interface IAiToolService
{
    IReadOnlyCollection<AiToolDefinition> GetEnabledToolDefinitions(IReadOnlyCollection<string>? enabledToolIds);

    Task<ChatToolExecutionResult?> TryExecuteToolCallAsync(
        AiToolCall toolCall,
        string userMessage,
        CancellationToken cancellationToken);
}
