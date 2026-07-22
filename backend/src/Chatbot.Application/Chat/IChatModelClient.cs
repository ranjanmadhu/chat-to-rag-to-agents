using Chatbot.Domain;

namespace Chatbot.Application.Chat;

public interface IChatModelClient
{
    Task<ChatModelResponse> SendAsync(IReadOnlyCollection<ChatMessage> messages, CancellationToken cancellationToken);
    IAsyncEnumerable<ChatStreamChunk> StreamAsync(IReadOnlyCollection<ChatMessage> messages, CancellationToken cancellationToken);
}
