using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Application.Chat;

public sealed class ChatService(IChatModelClientFactory chatModelClientFactory)
{
    private const string AssistantRole = "assistant";
    private const string UserRole = "user";

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        var messages = new[]
        {
            new ChatMessage(UserRole, request.Message.Trim())
        };

        var chatModelClient = chatModelClientFactory.Resolve(request.Provider);
        var response = await chatModelClient.SendAsync(messages, request.Model, cancellationToken);
        var content = string.IsNullOrWhiteSpace(response.Message.Content)
            ? "The model returned an empty response."
            : response.Message.Content.Trim();

        return new ChatResponse(
            content,
            response.Model,
            response.Metrics);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        var messages = new[]
        {
            new ChatMessage(UserRole, request.Message.Trim())
        };

        var chatModelClient = chatModelClientFactory.Resolve(request.Provider);

        await foreach (var responseChunk in chatModelClient.StreamAsync(messages, request.Model, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (responseChunk.IsDone)
            {
                yield return responseChunk;
                continue;
            }

            if (string.IsNullOrEmpty(responseChunk.Content))
            {
                continue;
            }

            yield return responseChunk;
        }
    }
}
