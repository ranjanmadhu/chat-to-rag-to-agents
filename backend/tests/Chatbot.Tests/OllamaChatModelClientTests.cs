using Chatbot.Domain;
using Chatbot.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace Chatbot.Tests;

public sealed class OllamaChatModelClientTests
{
    [Fact]
    public async Task SendAsync_When_Model_Not_Found_Returns_Friendly_Message()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("model not found")
            }));

        var client = CreateClient(handler);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, "llama3.2", null, CancellationToken.None);

        Assert.Contains("model shelf looks empty", result.Message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ollama pull llama3.2", result.Message.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_When_Timed_Out_Returns_Friendly_Message()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));
        var client = CreateClient(handler);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None);

        Assert.Contains("timeout wall", result.Message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ollama:RequestTimeoutSeconds", result.Message.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_When_Ollama_Is_Unavailable_Returns_Friendly_Message()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None);

        Assert.Contains("off duty", result.Message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ollama serve", result.Message.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaChatModelClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        return new OllamaChatModelClient(
            httpClient,
            Options.Create(new OllamaOptions
            {
                ChatModel = "llama3.2",
                RequestTimeoutSeconds = 60,
                BaseUrl = "http://localhost:11434"
            }),
            NullLogger<OllamaChatModelClient>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
