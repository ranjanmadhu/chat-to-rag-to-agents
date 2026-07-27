using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools;

public sealed class ToolOrchestrator(IChatToolService builtInToolExecutor) : IToolOrchestrator
{
    public IReadOnlyCollection<ChatToolDefinition> GetAvailableTools()
        => builtInToolExecutor.GetAvailableTools();

    public Task<ChatToolExecutionResult?> TryExecuteAsync(
        string message,
        IReadOnlyCollection<string>? enabledToolIds,
        CancellationToken cancellationToken)
        => builtInToolExecutor.TryExecuteAsync(message, enabledToolIds, cancellationToken);
}
