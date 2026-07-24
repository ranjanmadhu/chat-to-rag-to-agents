using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Application.Chat;

public sealed class ChatService(IChatModelClientFactory chatModelClientFactory)
{
    private const string AssistantRole = "assistant";
    private const string SystemRole = "system";
    private const string UserRole = "user";
    private const int MaxContextCharacters = 120_000;

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        var messages = BuildMessages(request);

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

        var messages = BuildMessages(request);

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

    private static IReadOnlyCollection<ChatMessage> BuildMessages(ChatRequest request)
    {
        var messages = new List<ChatMessage>(capacity: 2);

        if (!string.IsNullOrWhiteSpace(request.ContextText))
        {
            var trimmedContext = request.ContextText.Trim();
            if (trimmedContext.Length > MaxContextCharacters)
            {
                throw new ArgumentException(
                    $"Context text is too large ({trimmedContext.Length} chars). Limit is {MaxContextCharacters} chars.",
                    nameof(request));
            }

            var source = string.IsNullOrWhiteSpace(request.ContextFileName)
                ? "uploaded text file"
                : request.ContextFileName.Trim();

            messages.Add(new ChatMessage(
                SystemRole,
                $"Use the following context from {source} when answering the user. If the answer is not in this context, say so clearly.\n\n<context>\n{trimmedContext}\n</context>"));
        }

        messages.Add(new ChatMessage(UserRole, request.Message.Trim()));
        return messages;
    }
}
