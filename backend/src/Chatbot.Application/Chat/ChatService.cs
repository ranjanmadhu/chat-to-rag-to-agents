using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Application.Chat;

public sealed class ChatService(
    IChatModelClientFactory chatModelClientFactory,
    IContextWindowBudgetResolver contextWindowBudgetResolver,
    IContextTokenCounter contextTokenCounter)
{
    private const string AssistantRole = "assistant";
    private const string SystemRole = "system";
    private const string UserRole = "user";
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxContextImages = 4;

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        var messages = await BuildMessagesAsync(request, cancellationToken);

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

        var messages = await BuildMessagesAsync(request, cancellationToken);

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

    private async Task<IReadOnlyCollection<ChatMessage>> BuildMessagesAsync(
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>(capacity: 2);

        if (!string.IsNullOrWhiteSpace(request.ContextText))
        {
            var trimmedContext = request.ContextText.Trim();
            var maxContextTokens = contextWindowBudgetResolver.ResolveMaxContextTokens(request.Provider, request.Model);
            var countedTokens = await contextTokenCounter.CountTextTokensAsync(
                trimmedContext,
                request.Provider,
                request.Model,
                cancellationToken);

            if (countedTokens.HasValue && countedTokens.Value > maxContextTokens)
            {
                throw new ArgumentException(
                    $"Context text is too large ({countedTokens.Value} tokens). Limit for provider/model is {maxContextTokens} tokens.",
                    nameof(request));
            }

            if (!countedTokens.HasValue)
            {
                var maxContextCharacters = contextWindowBudgetResolver.ResolveMaxContextCharacters(request.Provider, request.Model);
                if (trimmedContext.Length > maxContextCharacters)
                {
                    throw new ArgumentException(
                        $"Context text is too large ({trimmedContext.Length} chars). Limit for provider/model is {maxContextCharacters} chars.",
                        nameof(request));
                }
            }

            var source = string.IsNullOrWhiteSpace(request.ContextFileName)
                ? "uploaded text file"
                : request.ContextFileName.Trim();

            messages.Add(new ChatMessage(
                SystemRole,
                $"Use the following context from {source} when answering the user. If the answer is not in this context, say so clearly.\n\n<context>\n{trimmedContext}\n</context>"));
        }

        var imageAttachments = BuildImageAttachments(request);
        messages.Add(new ChatMessage(
            UserRole,
            request.Message.Trim(),
            imageAttachments.Count == 0 ? null : imageAttachments));
        return messages;
    }

    private static IReadOnlyCollection<ChatImageAttachment> BuildImageAttachments(ChatRequest request)
    {
        var attachments = new List<ChatImageAttachment>(capacity: MaxContextImages);

        if (request.ContextImages is not null)
        {
            foreach (var image in request.ContextImages)
            {
                attachments.Add(BuildImageAttachment(image.Base64, image.MimeType, image.FileName, nameof(request)));
            }
        }

        var hasAnyLegacyImageField =
            !string.IsNullOrWhiteSpace(request.ContextImageBase64) ||
            !string.IsNullOrWhiteSpace(request.ContextImageMimeType) ||
            !string.IsNullOrWhiteSpace(request.ContextImageFileName);

        if (hasAnyLegacyImageField)
        {
            attachments.Add(BuildImageAttachment(
                request.ContextImageBase64,
                request.ContextImageMimeType,
                request.ContextImageFileName,
                nameof(request)));
        }

        if (attachments.Count > MaxContextImages)
        {
            throw new ArgumentException(
                $"Too many context images ({attachments.Count}). Limit is {MaxContextImages}.",
                nameof(request));
        }

        return attachments;
    }

    private static ChatImageAttachment BuildImageAttachment(
        string? base64,
        string? mimeType,
        string? fileName,
        string argumentName)
    {
        if (string.IsNullOrWhiteSpace(base64) || string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Image context is incomplete. Provide both Base64 and MimeType.",
                argumentName);
        }

        var normalizedMimeType = mimeType.Trim();
        var normalizedBase64 = base64.Trim();

        if (string.IsNullOrWhiteSpace(normalizedBase64))
        {
            throw new ArgumentException("Image base64 data is required.", argumentName);
        }

        if (normalizedBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = normalizedBase64.IndexOf(',');
            if (commaIndex >= 0 && commaIndex < normalizedBase64.Length - 1)
            {
                normalizedBase64 = normalizedBase64[(commaIndex + 1)..];
            }
        }

        var mimeTypeValue = normalizedMimeType;
        if (!mimeTypeValue.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("ContextImageMimeType must be an image MIME type.", argumentName);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(normalizedBase64);
        }
        catch (FormatException)
        {
            throw new ArgumentException("ContextImageBase64 is not valid base64.", argumentName);
        }

        if (bytes.Length > MaxImageBytes)
        {
            throw new ArgumentException(
                $"Image context is too large ({bytes.Length} bytes). Limit is {MaxImageBytes} bytes.",
                argumentName);
        }

        var resolvedFileName = string.IsNullOrWhiteSpace(fileName)
            ? "uploaded-image"
            : fileName.Trim();

        return new ChatImageAttachment(mimeTypeValue, normalizedBase64, resolvedFileName);
    }
}
