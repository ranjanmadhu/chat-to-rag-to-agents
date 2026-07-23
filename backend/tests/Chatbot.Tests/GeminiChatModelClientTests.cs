using Chatbot.Application.Chat;
using Chatbot.Domain;
using Chatbot.Infrastructure.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.Tests;

public sealed class GeminiChatModelClientTests
{
    [Fact]
    public async Task SendAsync_Uses_Gemini_API_And_Returns_Content()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          { "text": "hello from gemini" }
                        ],
                        "role": "model"
                      }
                    }
                  ],
                  "usageMetadata": {
                    "promptTokenCount": 3,
                    "candidatesTokenCount": 5,
                    "totalTokenCount": 8
                  }
                }
                """)
            });
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com")
        };
        var client = new GeminiChatModelClient(
            httpClient,
            Options.Create(new GeminiOptions
            {
                ApiKey = "test-key",
                Model = "gemini-2.0-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Contains("/v1beta/models/gemini-2.0-flash:generateContent", capturedRequest.RequestUri!.ToString());
        Assert.Contains("key=test-key", capturedRequest.RequestUri!.Query);
        Assert.Equal("hello from gemini", result.Message.Content);
        Assert.Equal(3, result.Metrics?.InputTokens);
        Assert.Equal(5, result.Metrics?.OutputTokens);
        Assert.True(result.Metrics?.OutputTokensPerSecond > 0);
    }

    [Fact]
    public async Task StreamAsync_Uses_Sse_And_Returns_Content_Chunks()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                data: {"candidates":[{"content":{"parts":[{"text":"hello "}]}}]}

                data: {"candidates":[{"content":{"parts":[{"text":"from stream"}]}}],"usageMetadata":{"promptTokenCount":4,"candidatesTokenCount":6,"totalTokenCount":10}}

                """)
            });
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com")
        };
        var client = new GeminiChatModelClient(
            httpClient,
            Options.Create(new GeminiOptions
            {
                ApiKey = "test-key",
                Model = "gemini-2.0-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in client.StreamAsync(new[] { new ChatMessage("user", "hi") }, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.NotNull(capturedRequest);
        Assert.Contains("/v1beta/models/gemini-2.0-flash:streamGenerateContent", capturedRequest!.RequestUri!.ToString());
        Assert.Contains("alt=sse", capturedRequest.RequestUri!.Query);
        Assert.Equal("hello from stream", string.Concat(chunks.Where(chunk => !chunk.IsDone).Select(chunk => chunk.Content)));
        Assert.True(chunks.Last().IsDone);
        Assert.Equal(4, chunks.Last().Metrics?.InputTokens);
        Assert.Equal(6, chunks.Last().Metrics?.OutputTokens);
        Assert.True(chunks.Last().Metrics?.OutputTokensPerSecond > 0);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
