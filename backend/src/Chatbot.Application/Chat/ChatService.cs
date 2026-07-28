using Chatbot.Domain;
using Chatbot.Application.Tools;
using System.Runtime.CompilerServices;

namespace Chatbot.Application.Chat;

public sealed class ChatService(
    IChatModelClientFactory chatModelClientFactory,
    IContextWindowBudgetResolver contextWindowBudgetResolver,
    IContextTokenCounter contextTokenCounter,
    IToolOrchestrator toolOrchestrator,
    IAiToolService aiToolService,
    IToolCallRelevancePolicy toolCallRelevancePolicy)
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

        var normalizedEnabledToolIds = (request.EnabledToolIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray();

        var messages = await BuildMessagesAsync(request, cancellationToken);
        var enabledToolDefinitions = aiToolService.GetEnabledToolDefinitions(normalizedEnabledToolIds);

        var chatModelClient = chatModelClientFactory.Resolve(request.Provider);
        var response = await chatModelClient.SendAsync(messages, request.Model, enabledToolDefinitions, cancellationToken);
        var rejectedToolCallReasons = new List<string>();
        var hadModelToolCalls = response.ToolCalls is { Count: > 0 };

        if (response.ToolCalls is { Count: > 0 } toolCalls)
        {
            foreach (var toolCall in toolCalls)
            {
                var validation = toolCallRelevancePolicy.Validate(toolCall, request.Message, enabledToolDefinitions);
                if (!validation.IsValid)
                {
                    rejectedToolCallReasons.Add(validation.Reason);
                    continue;
                }

                var toolResult = await aiToolService.TryExecuteToolCallAsync(
                    toolCall,
                    request.Message,
                    cancellationToken);
                if (toolResult is not null)
                {
                    return new ChatResponse(
                        toolResult.Output,
                        $"tool:{toolResult.ToolId}",
                        Metrics: response.Metrics,
                        Observability: new ToolObservability(
                            DecisionSource: "ai",
                            Summary: $"AI selected '{toolResult.ToolId}' from {normalizedEnabledToolIds.Length} enabled tool(s).",
                            Steps:
                            [
                                $"Enabled tools: {normalizedEnabledToolIds.Length}",
                                validation.Reason,
                                $"Model called: {toolCall.ToolId}",
                                $"Executed: {toolResult.ToolId}"
                            ],
                            UsedToolId: toolResult.ToolId,
                            EnabledToolIds: normalizedEnabledToolIds));
                }

                rejectedToolCallReasons.Add(
                    $"Rejected: tool '{toolCall.ToolId}' passed validation but execution returned no result.");
            }
        }

        if (enabledToolDefinitions.Count > 0 && !hadModelToolCalls)
        {
            var deterministicFallback = await toolOrchestrator.TryExecuteAsync(
                request.Message,
                normalizedEnabledToolIds,
                cancellationToken);

            if (deterministicFallback is not null)
            {
                return new ChatResponse(
                    deterministicFallback.Output,
                    $"tool:{deterministicFallback.ToolId}",
                    Metrics: response.Metrics,
                    Observability: new ToolObservability(
                        DecisionSource: "deterministic-fallback",
                        Summary: $"Fallback matched '{deterministicFallback.ToolId}' after no executable model tool call.",
                        Steps:
                        [
                            $"Enabled tools: {normalizedEnabledToolIds.Length}",
                            "No executable model tool call",
                            $"Fallback executed: {deterministicFallback.ToolId}"
                        ],
                        UsedToolId: deterministicFallback.ToolId,
                        EnabledToolIds: normalizedEnabledToolIds));
            }
        }

        // Some providers return only tool calls with empty assistant text. If all calls were rejected
        // or produced no executable result, request a plain model answer without tools.
        if (hadModelToolCalls && string.IsNullOrWhiteSpace(response.Message.Content))
        {
            response = await chatModelClient.SendAsync(messages, request.Model, [], cancellationToken);
            rejectedToolCallReasons.Add("Requested follow-up model answer without tools.");
        }

        var content = string.IsNullOrWhiteSpace(response.Message.Content)
            ? "The model returned an empty response."
            : response.Message.Content.Trim();

        var hasEnabledTools = normalizedEnabledToolIds.Length > 0;
        var rejectedReason = rejectedToolCallReasons.Count > 0
            ? string.Join(" ", rejectedToolCallReasons)
            : null;
        var observability = hasEnabledTools
            ? new ToolObservability(
                DecisionSource: "none",
                Summary: $"AI answered directly; no tool was executed ({normalizedEnabledToolIds.Length} enabled).",
                Steps:
                [
                    $"Enabled tools: {normalizedEnabledToolIds.Length}",
                    rejectedToolCallReasons.Count > 0
                        ? $"Rejected model tool calls: {rejectedToolCallReasons.Count}"
                        : "No executable tool call",
                    "Returned model output"
                ],
                NotUsedReason: rejectedReason ?? "No enabled tool was selected for this request.",
                EnabledToolIds: normalizedEnabledToolIds)
            : null;

        return new ChatResponse(
            content,
            response.Model,
            response.Metrics,
            Observability: observability);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Message is required.", nameof(request));
        }

        var enabledToolDefinitions = aiToolService.GetEnabledToolDefinitions(request.EnabledToolIds);
        if (enabledToolDefinitions.Count > 0)
        {
            var response = await SendAsync(request, cancellationToken);
            yield return new ChatStreamChunk(response.Message, Model: response.Model);
            yield return new ChatStreamChunk(
                string.Empty,
                IsDone: true,
                Model: response.Model,
                Metrics: response.Metrics,
                Observability: response.Observability);
            yield break;
        }

        var messages = await BuildMessagesAsync(request, cancellationToken);

        var chatModelClient = chatModelClientFactory.Resolve(request.Provider);

        await foreach (var responseChunk in chatModelClient.StreamAsync(messages, request.Model, enabledToolDefinitions, cancellationToken)
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
