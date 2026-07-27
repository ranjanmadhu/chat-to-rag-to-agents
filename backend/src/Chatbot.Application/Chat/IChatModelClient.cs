using Chatbot.Domain;
using Chatbot.Application.Tools;

namespace Chatbot.Application.Chat;

public interface IChatModelClient
{
    Task<ChatModelResponse> SendAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        IReadOnlyCollection<AiToolDefinition>? tools,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        IReadOnlyCollection<AiToolDefinition>? tools,
        CancellationToken cancellationToken);
}
