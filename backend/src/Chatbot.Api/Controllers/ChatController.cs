using Chatbot.Application.Chat;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController(ChatService chatService, ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await chatService.SendAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("stream")]
    public async Task<IActionResult> StreamAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message is required." });
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            ChatMetrics? metrics = null;

            await foreach (var chunk in chatService.StreamAsync(request, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    await WriteSseEventAsync(Response, "chunk", new { content = chunk.Content }, cancellationToken);
                }

                if (chunk.IsDone)
                {
                    metrics = chunk.Metrics;
                }
            }

            await WriteSseEventAsync(Response, "done", new { metrics }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Chat stream canceled by client.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Streaming chat failed.");
            await WriteSseEventAsync(
                Response,
                "error",
                new { error = "Streaming failed. Check provider/backend logs and try again." },
                cancellationToken);
        }

        return new EmptyResult();
    }

    private static async Task WriteSseEventAsync(
        HttpResponse response,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
