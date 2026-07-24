using Chatbot.Domain;

namespace Chatbot.Application.Chat;

public interface IChatModelClient
{
    Task<ChatModelResponse> SendAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        IReadOnlyCollection<ChatMessage> messages,
        string? model,
        CancellationToken cancellationToken);
}
