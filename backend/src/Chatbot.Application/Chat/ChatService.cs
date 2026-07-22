using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Application.Chat;

public sealed class ChatService(IChatModelClient chatModelClient)
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

        var response = await chatModelClient.SendAsync(messages, cancellationToken);
        var content = string.IsNullOrWhiteSpace(response.Message.Content)
            ? "The model returned an empty response."
            : response.Message.Content.Trim();

        return new ChatResponse(
            content,
            response.Message.Role == AssistantRole ? "ollama" : response.Message.Role,
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

        await foreach (var responseChunk in chatModelClient.StreamAsync(messages, cancellationToken)
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
