using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Ollama;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController(
    ChatService chatService,
    IOllamaModelAdminClient ollamaModelAdminClient,
    IOllamaWarmupState ollamaWarmupState,
    ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyCollection<ModelProfile> RecommendedModels =
    [
        new("deepseek-r1:1.5b", "DeepSeek R1 1.5B", true, false),
        new("gemma2:2b", "Gemma 2 2B", true, false),
        new("phi3:mini", "Phi-3 Mini", true, false),
        new("llama3.2:1b", "Llama 3.2 1B", true, false),
        new("llama3.2:3b", "Llama 3.2 3B", true, false)
    ];

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> SendAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (TryBuildWarmupBlockResponse(request, out var warmupBlock))
        {
            return warmupBlock;
        }

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
        if (TryBuildWarmupBlockResponse(request, out var warmupBlock))
        {
            return warmupBlock;
        }

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
            string? model = null;

            await foreach (var chunk in chatService.StreamAsync(request, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    await WriteSseEventAsync(Response, "chunk", new { content = chunk.Content }, cancellationToken);
                }

                if (chunk.IsDone)
                {
                    metrics = chunk.Metrics;
                    model = chunk.Model;
                }
            }

            await WriteSseEventAsync(Response, "done", new { metrics, model }, cancellationToken);
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

    [HttpGet("ollama/models")]
    public async Task<ActionResult<IReadOnlyCollection<object>>> GetOllamaModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var installed = await ollamaModelAdminClient.ListInstalledModelsAsync(cancellationToken);
            var installedByName = installed.ToDictionary(model => model.Name, StringComparer.OrdinalIgnoreCase);

            var recommended = RecommendedModels.Select(model => new
            {
                model = model.Name,
                label = model.Label,
                supportsText = installedByName.TryGetValue(model.Name, out var installedModel)
                    ? installedModel.SupportsText
                    : model.SupportsText,
                supportsImage = installedByName.TryGetValue(model.Name, out var installedVisionModel)
                    ? installedVisionModel.SupportsImage
                    : model.SupportsImage,
                isInstalled = installedByName.ContainsKey(model.Name),
                isRecommended = true
            });

            var customInstalled = installed
                .Where(installedModel => RecommendedModels.All(model => !string.Equals(model.Name, installedModel.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(installedModel => new
                {
                    model = installedModel.Name,
                    label = installedModel.Name,
                    supportsText = installedModel.SupportsText,
                    supportsImage = installedModel.SupportsImage,
                    isInstalled = true,
                    isRecommended = false
                });

            return Ok(recommended.Concat(customInstalled).ToArray());
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Unable to query Ollama models. Ensure ollama serve is running."
            });
        }
    }

    [HttpPost("ollama/warmup")]
    public async Task<IActionResult> WarmupOllamaModelAsync([FromBody] OllamaWarmupRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return BadRequest(new { error = "Model is required." });
        }

        ollamaWarmupState.Begin(request.Model);
        var result = await ollamaModelAdminClient.WarmupAsync(request.Model, cancellationToken);
        ollamaWarmupState.Complete(result.Model, result.IsReady, result.Error);

        if (!result.IsReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Error, model = result.Model });
        }

        return Ok(new { status = "ready", model = result.Model });
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

    private bool TryBuildWarmupBlockResponse(ChatRequest request, out ObjectResult response)
    {
        response = null!;

        if (!string.Equals(request.Provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return false;
        }

        var status = ollamaWarmupState.Get(request.Model);
        if (!status.IsWarming)
        {
            return false;
        }

        response = StatusCode(StatusCodes.Status409Conflict, new
        {
            error = $"Model '{status.Model}' is still warming up. Please wait until warmup completes.",
            model = status.Model,
            status = "warming"
        })!;
        return true;
    }

    public sealed record OllamaWarmupRequest(string Model);

    private sealed record ModelProfile(string Name, string Label, bool SupportsText, bool SupportsImage);
}
