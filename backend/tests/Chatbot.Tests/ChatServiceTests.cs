using Chatbot.Application.Chat;
using Chatbot.Application.Tools;
using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Tests;

public sealed class ChatServiceTests
{
    private const int DefaultContextChars = 120_000;

    [Fact]
    public async Task SendAsync_Returns_Model_Response()
    {
        var service = CreateService(new StubChatModelClient("Hello from the model."));

        var response = await service.SendAsync(new ChatRequest("Hello"), CancellationToken.None);

        Assert.Equal("Hello from the model.", response.Message);
        Assert.Equal("stub-model", response.Model);
    }

    [Fact]
    public async Task SendAsync_Rejects_Empty_Message()
    {
        var service = CreateService(new StubChatModelClient("Unused"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(new ChatRequest(" "), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Includes_Text_Context_When_Provided()
    {
        var service = CreateService(
            new StubChatModelClient(
                "Context-aware response.",
                messages =>
                {
                    Assert.Contains(messages, message => message.Role == "system" && message.Content.Contains("Q3 roadmap"));
                    Assert.Contains(messages, message => message.Role == "user" && message.Content == "Hello");
                }));

        var response = await service.SendAsync(
            new ChatRequest("Hello", ContextText: "Q3 roadmap: launch search and analytics", ContextFileName: "roadmap.txt"),
            CancellationToken.None);

        Assert.Equal("Context-aware response.", response.Message);
    }

    [Fact]
    public async Task SendAsync_Includes_Image_Context_When_Provided()
    {
        var imageBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var service = CreateService(
            new StubChatModelClient(
                "Image-aware response.",
                messages =>
                {
                    var user = Assert.Single(messages.Where(message => message.Role == "user"));
                    var image = Assert.Single(user.Images!);
                    Assert.Equal("image/png", image.MimeType);
                    Assert.Equal(imageBase64, image.Base64Data);
                    Assert.Equal("chart.png", image.FileName);
                }));

        var response = await service.SendAsync(
            new ChatRequest("Explain this chart", ContextImageBase64: imageBase64, ContextImageMimeType: "image/png", ContextImageFileName: "chart.png"),
            CancellationToken.None);

        Assert.Equal("Image-aware response.", response.Message);
    }

    [Fact]
    public async Task SendAsync_Rejects_Incomplete_Image_Context()
    {
        var service = CreateService(new StubChatModelClient("Unused"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(
                new ChatRequest("Hello", ContextImageBase64: "aGVsbG8="),
                CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Includes_Multiple_Image_Context_When_Provided()
    {
        var service = CreateService(
            new StubChatModelClient(
                "Multi-image response.",
                messages =>
                {
                    var user = Assert.Single(messages.Where(message => message.Role == "user"));
                    Assert.NotNull(user.Images);
                    Assert.Equal(3, user.Images.Count);
                }));

        var request = new ChatRequest(
            "Compare these screenshots",
            ContextImages:
            [
                new ChatRequestImage("AQID", "image/png", "one.png"),
                new ChatRequestImage("BAUG", "image/png", "two.png"),
                new ChatRequestImage("BwgJ", "image/png", "three.png")
            ]);

        var response = await service.SendAsync(request, CancellationToken.None);
        Assert.Equal("Multi-image response.", response.Message);
    }

    [Fact]
    public async Task SendAsync_Rejects_More_Than_Four_Context_Images()
    {
        var service = CreateService(new StubChatModelClient("Unused"));

        var request = new ChatRequest(
            "Analyze",
            ContextImages:
            [
                new ChatRequestImage("AQID", "image/png"),
                new ChatRequestImage("BAUG", "image/png"),
                new ChatRequestImage("BwgJ", "image/png"),
                new ChatRequestImage("CgsM", "image/png"),
                new ChatRequestImage("DQ4P", "image/png")
            ]);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_Rejects_Context_That_Exceeds_Model_Dynamic_Limit()
    {
        var service = CreateService(new StubChatModelClient("Unused"), maxContextCharacters: 50);
        var oversizedContext = new string('x', 51);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(
                new ChatRequest("Hello", Provider: "gemini", Model: "gemini-3.6-flash", ContextText: oversizedContext),
                CancellationToken.None));

        Assert.Contains("Limit for provider/model is 50 chars", ex.Message);
    }

    [Fact]
    public async Task SendAsync_Uses_Tool_When_Enabled_And_Message_Matches()
    {
        var aiToolService = new StubAiToolService(
            [new AiToolDefinition("calculator", "desc", [new AiToolParameterDefinition("expression", "string", "desc", true)])],
            (_, _) => new ChatToolExecutionResult("calculator", "Result: 42"));

        var service = CreateService(
            new StubChatModelClient(
                "Tool call requested",
                toolCalls: [new AiToolCall("calculator", "{\"expression\":\"6 * 7\"}")]),
            new StubToolOrchestrator(_ => null),
            aiToolService);

        var response = await service.SendAsync(
            new ChatRequest("calculate 6 * 7", EnabledToolIds: ["calculator"]),
            CancellationToken.None);

        Assert.Equal("Result: 42", response.Message);
        Assert.Equal("tool:calculator", response.Model);
        Assert.Equal("calculator", response.Observability?.UsedToolId);
        Assert.Equal("ai", response.Observability?.DecisionSource);
    }

    [Fact]
    public async Task SendAsync_Falls_Back_To_Model_When_No_Tool_Result()
    {
        var service = CreateService(
            new StubChatModelClient("Hello from model fallback."),
            new StubToolOrchestrator(_ => null));

        var response = await service.SendAsync(
            new ChatRequest("Hello", EnabledToolIds: ["calculator"]),
            CancellationToken.None);

        Assert.Equal("Hello from model fallback.", response.Message);
        Assert.Equal("stub-model", response.Model);
        Assert.Equal("none", response.Observability?.DecisionSource);
        Assert.Null(response.Observability?.UsedToolId);
    }

    [Fact]
    public async Task StreamAsync_With_Tools_Emits_Done_Metrics()
    {
        var metrics = new ChatMetrics(12, 8, 4.0);
        var aiToolService = new StubAiToolService(
            [new AiToolDefinition("calculator", "desc", [new AiToolParameterDefinition("expression", "string", "desc", true)])],
            (_, _) => new ChatToolExecutionResult("calculator", "Result: 42"));

        var service = CreateService(
            new StubChatModelClient(
                "Tool call requested",
                toolCalls: [new AiToolCall("calculator", "{\"expression\":\"6 * 7\"}")],
                metrics: metrics),
            new StubToolOrchestrator(_ => null),
            aiToolService);

        ChatStreamChunk? doneChunk = null;

        await foreach (var chunk in service.StreamAsync(
                           new ChatRequest("calculate 6 * 7", EnabledToolIds: ["calculator"]),
                           CancellationToken.None))
        {
            if (chunk.IsDone)
            {
                doneChunk = chunk;
            }
        }

        Assert.NotNull(doneChunk);
        Assert.Equal(metrics, doneChunk!.Metrics);
    }

    [Fact]
    public async Task SemanticKernelAiToolService_DateTool_Respects_User_Message_Relevance()
    {
        var service = new SemanticKernelAiToolService(new PassThroughDateTimeChatToolService());

        var result = await service.TryExecuteToolCallAsync(
            new AiToolCall("date", "{}"),
            "who was the last president",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SendAsync_Rejected_Model_ToolCall_Uses_Model_Response_Without_Fallback()
    {
        var aiToolInvoked = false;
        var fallbackInvoked = false;

        var aiToolService = new StubAiToolService(
            [new AiToolDefinition("date", "desc", [])],
            (_, _) =>
            {
                aiToolInvoked = true;
                return new ChatToolExecutionResult("date", "Current UTC date: 2026-07-27");
            });

        var service = CreateService(
            new StubChatModelClient(
                "Model answer should be used.",
                toolCalls: [new AiToolCall("date", "{}")]),
            new StubToolOrchestrator(_ =>
            {
                fallbackInvoked = true;
                return new ChatToolExecutionResult("date", "Fallback date");
            }),
            aiToolService);

        var response = await service.SendAsync(
            new ChatRequest("who was the last president", EnabledToolIds: ["date"]),
            CancellationToken.None);

        Assert.False(aiToolInvoked);
        Assert.False(fallbackInvoked);
        Assert.Equal("Model answer should be used.", response.Message);
        Assert.Equal("stub-model", response.Model);
        Assert.Equal("none", response.Observability?.DecisionSource);
        Assert.Contains("Rejected:", response.Observability?.NotUsedReason);
    }

    [Fact]
    public async Task SendAsync_Rejected_ToolCall_With_Empty_First_Response_Uses_FollowUp_Model_Answer()
    {
        var client = new SequencedChatModelClient(
            new ChatModelResponse(
                new ChatMessage("assistant", string.Empty),
                "stub-model",
                ToolCalls: [new AiToolCall("date", "{}")]),
            new ChatModelResponse(
                new ChatMessage("assistant", "Direct model answer."),
                "stub-model"));

        var service = CreateService(
            client,
            new StubToolOrchestrator(_ => new ChatToolExecutionResult("date", "Fallback date")),
            new StubAiToolService(
                [new AiToolDefinition("date", "desc", [])],
                (_, _) => null));

        var response = await service.SendAsync(
            new ChatRequest("who was the last president", EnabledToolIds: ["date"]),
            CancellationToken.None);

        Assert.Equal("Direct model answer.", response.Message);
        Assert.Equal(2, client.SendCount);
        Assert.Equal("none", response.Observability?.DecisionSource);
        Assert.Contains("Requested follow-up model answer without tools.", response.Observability?.NotUsedReason);
    }

    private static ChatService CreateService(
        IChatModelClient chatModelClient,
        IToolOrchestrator? toolOrchestrator = null,
        IAiToolService? aiToolService = null,
        IToolCallRelevancePolicy? toolCallRelevancePolicy = null,
        int maxContextCharacters = DefaultContextChars)
    {
        return new ChatService(
            new StubChatModelClientFactory(chatModelClient),
            new StubContextWindowBudgetResolver(maxContextCharacters),
            new StubContextTokenCounter(),
            toolOrchestrator ?? new StubToolOrchestrator(_ => null),
            aiToolService ?? new StubAiToolService([], (_, _) => null),
            toolCallRelevancePolicy ?? new ToolCallRelevancePolicy(
                [
                    new DateToolCallRelevanceValidator(),
                    new TimeToolCallRelevanceValidator(),
                    new CalculatorToolCallRelevanceValidator()
                ]));
    }

    private sealed class StubChatModelClientFactory(IChatModelClient chatModelClient) : IChatModelClientFactory
    {
        public IChatModelClient Resolve(string? provider) => chatModelClient;
    }

    private sealed class StubContextWindowBudgetResolver(int maxContextCharacters) : IContextWindowBudgetResolver
    {
        public int ResolveMaxContextTokens(string? provider, string? model) => int.MaxValue;

        public int ResolveMaxContextCharacters(string? provider, string? model) => maxContextCharacters;
    }

    private sealed class StubContextTokenCounter : IContextTokenCounter
    {
        public Task<int?> CountTextTokensAsync(string text, string? provider, string? model, CancellationToken cancellationToken)
            => Task.FromResult<int?>(null);
    }

    private sealed class StubChatModelClient(
        string content,
        Action<IReadOnlyCollection<ChatMessage>>? validator = null,
        IReadOnlyCollection<AiToolCall>? toolCalls = null,
        ChatMetrics? metrics = null) : IChatModelClient
    {
        public Task<ChatModelResponse> SendAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            IReadOnlyCollection<AiToolDefinition>? tools,
            CancellationToken cancellationToken)
        {
            validator?.Invoke(messages);
            Assert.Contains(messages, message => message.Role == "user" && !string.IsNullOrWhiteSpace(message.Content));
            return Task.FromResult(new ChatModelResponse(new ChatMessage("assistant", content), model ?? "stub-model", metrics, toolCalls));
        }

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            IReadOnlyCollection<AiToolDefinition>? tools,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            validator?.Invoke(messages);
            Assert.Contains(messages, message => message.Role == "user" && !string.IsNullOrWhiteSpace(message.Content));
            await Task.Yield();
            yield return new ChatStreamChunk(content, Model: model ?? "stub-model");
            yield return new ChatStreamChunk(string.Empty, IsDone: true, Model: model ?? "stub-model");
        }
    }

    private sealed class StubToolOrchestrator(Func<string, ChatToolExecutionResult?> resolver) : IToolOrchestrator
    {
        public IReadOnlyCollection<ChatToolDefinition> GetAvailableTools()
            => [new ChatToolDefinition("calculator", "Calculator", "desc", ["calc 1+1"])];

        public Task<ChatToolExecutionResult?> TryExecuteAsync(
            string message,
            IReadOnlyCollection<string>? enabledToolIds,
            CancellationToken cancellationToken)
            => Task.FromResult(resolver(message));
    }

    private sealed class StubAiToolService(
        IReadOnlyCollection<AiToolDefinition> enabledDefinitions,
        Func<AiToolCall, string, ChatToolExecutionResult?> resolver) : IAiToolService
    {
        public IReadOnlyCollection<AiToolDefinition> GetEnabledToolDefinitions(IReadOnlyCollection<string>? enabledToolIds)
            => enabledDefinitions;

        public Task<ChatToolExecutionResult?> TryExecuteToolCallAsync(
            AiToolCall toolCall,
            string userMessage,
            CancellationToken cancellationToken)
            => Task.FromResult(resolver(toolCall, userMessage));
    }

    private sealed class SequencedChatModelClient(params ChatModelResponse[] responses) : IChatModelClient
    {
        private readonly Queue<ChatModelResponse> queue = new(responses);

        public int SendCount { get; private set; }

        public Task<ChatModelResponse> SendAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            IReadOnlyCollection<AiToolDefinition>? tools,
            CancellationToken cancellationToken)
        {
            SendCount++;
            if (queue.Count == 0)
            {
                throw new InvalidOperationException("No queued model responses available.");
            }

            return Task.FromResult(queue.Dequeue());
        }

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            IReadOnlyCollection<AiToolDefinition>? tools,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ChatStreamChunk("unused", Model: model ?? "stub-model");
            yield return new ChatStreamChunk(string.Empty, IsDone: true, Model: model ?? "stub-model");
        }
    }

    private sealed class PassThroughDateTimeChatToolService : IChatToolService
    {
        public IReadOnlyCollection<ChatToolDefinition> GetAvailableTools() => [];

        public Task<ChatToolExecutionResult?> TryExecuteAsync(
            string message,
            IReadOnlyCollection<string>? enabledToolIds,
            CancellationToken cancellationToken)
        {
            var normalized = message.Trim().ToLowerInvariant();
            if ((enabledToolIds?.Contains("date") ?? false) && (normalized.Contains("date") || normalized.Contains("today")))
            {
                return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult("date", "Current UTC date: 2026-07-27"));
            }

            if ((enabledToolIds?.Contains("time") ?? false) && (normalized.Contains("time") || normalized.Contains("clock")))
            {
                return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult("time", "Current UTC time: 11:46:14 (UTC)"));
            }

            return Task.FromResult<ChatToolExecutionResult?>(null);
        }
    }
}
