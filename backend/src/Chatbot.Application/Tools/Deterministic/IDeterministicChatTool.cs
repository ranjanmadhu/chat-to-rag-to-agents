using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools.Deterministic;

public interface IDeterministicChatTool
{
    string ToolId { get; }

    ChatToolDefinition Definition { get; }

    Task<ChatToolExecutionResult?> TryExecuteAsync(string message, CancellationToken cancellationToken);
}
