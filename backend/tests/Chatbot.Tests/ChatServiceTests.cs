using Chatbot.Application.Chat;
using Chatbot.Domain;
using System.Runtime.CompilerServices;

namespace Chatbot.Tests;

public sealed class ChatServiceTests
{
    [Fact]
    public async Task SendAsync_Returns_Model_Response()
    {
        var service = new ChatService(new StubChatModelClientFactory(new StubChatModelClient("Hello from the model.")));

        var response = await service.SendAsync(new ChatRequest("Hello"), CancellationToken.None);

        Assert.Equal("Hello from the model.", response.Message);
        Assert.Equal("stub-model", response.Model);
    }

    [Fact]
    public async Task SendAsync_Rejects_Empty_Message()
    {
        var service = new ChatService(new StubChatModelClientFactory(new StubChatModelClient("Unused")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync(new ChatRequest(" "), CancellationToken.None));
    }

    private sealed class StubChatModelClientFactory(IChatModelClient chatModelClient) : IChatModelClientFactory
    {
        public IChatModelClient Resolve(string? provider) => chatModelClient;
    }

    private sealed class StubChatModelClient(string content) : IChatModelClient
    {
        public Task<ChatModelResponse> SendAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            CancellationToken cancellationToken)
        {
            Assert.Contains(messages, message => message.Role == "user" && message.Content == "Hello");
            return Task.FromResult(new ChatModelResponse(new ChatMessage("assistant", content), model ?? "stub-model"));
        }

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
            IReadOnlyCollection<ChatMessage> messages,
            string? model,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            Assert.Contains(messages, message => message.Role == "user" && message.Content == "Hello");
            await Task.Yield();
            yield return new ChatStreamChunk(content, Model: model ?? "stub-model");
            yield return new ChatStreamChunk(string.Empty, IsDone: true, Model: model ?? "stub-model");
        }
    }
}
