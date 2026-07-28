using Chatbot.Application.Chat;
using Chatbot.Domain;
using Chatbot.Infrastructure.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

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
                Model = "gemini-3.6-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Contains("/v1beta/models/gemini-3.6-flash:generateContent", capturedRequest.RequestUri!.ToString());
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
                Model = "gemini-3.6-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in client.StreamAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.NotNull(capturedRequest);
        Assert.Contains("/v1beta/models/gemini-3.6-flash:streamGenerateContent", capturedRequest!.RequestUri!.ToString());
        Assert.Contains("alt=sse", capturedRequest.RequestUri!.Query);
        Assert.Equal("hello from stream", string.Concat(chunks.Where(chunk => !chunk.IsDone).Select(chunk => chunk.Content)));
        Assert.True(chunks.Last().IsDone);
        Assert.Equal(4, chunks.Last().Metrics?.InputTokens);
        Assert.Equal(6, chunks.Last().Metrics?.OutputTokens);
        Assert.True(chunks.Last().Metrics?.OutputTokensPerSecond > 0);
    }

    [Fact]
    public async Task SendAsync_Serializes_Image_Attachments_As_InlineData()
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
                          { "text": "image understood" }
                        ]
                      }
                    }
                  ]
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
                Model = "gemini-3.6-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        _ = await client.SendAsync(
            new[]
            {
                new ChatMessage(
                    "user",
                    "What is in this image?",
                    new[] { new ChatImageAttachment("image/png", "AQIDBA==", "photo.png") })
            },
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(capturedRequest);
        var body = await capturedRequest!.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var firstPart = json.RootElement
            .GetProperty("contents")[0]
            .GetProperty("parts")[1]
            .GetProperty("inlineData");

        Assert.Equal("image/png", firstPart.GetProperty("mimeType").GetString());
        Assert.Equal("AQIDBA==", firstPart.GetProperty("data").GetString());
    }

    [Fact]
    public async Task SendAsync_When_Quota_Is_Exceeded_Returns_Friendly_429_Message()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("quota exceeded")
            }));

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com")
        };

        var client = new GeminiChatModelClient(
            httpClient,
            Options.Create(new GeminiOptions
            {
                ApiKey = "test-key",
                Model = "gemini-3.6-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var result = await client.SendAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None);

        Assert.Contains("burned through Madhu's credits", result.Message.Content);
        Assert.Contains("try again soon", result.Message.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamAsync_When_Quota_Is_Exceeded_Returns_Friendly_429_Message_Then_Done()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("quota exceeded")
            }));

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com")
        };

        var client = new GeminiChatModelClient(
            httpClient,
            Options.Create(new GeminiOptions
            {
                ApiKey = "test-key",
                Model = "gemini-3.6-flash",
                BaseUrl = "https://generativelanguage.googleapis.com"
            }),
            NullLogger<GeminiChatModelClient>.Instance);

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in client.StreamAsync(new[] { new ChatMessage("user", "hi") }, null, null, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.Contains("burned through Madhu's credits", chunks[0].Content);
        Assert.True(chunks.Last().IsDone);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
