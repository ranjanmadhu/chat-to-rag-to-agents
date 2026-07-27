using Chatbot.Application.Chat;
using Chatbot.Infrastructure.Ollama;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using UglyToad.PdfPig;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController(
    ChatService chatService,
    IChatToolService chatToolService,
    IContextWindowBudgetResolver contextWindowBudgetResolver,
    IContextTokenCounter contextTokenCounter,
    IOllamaModelAdminClient ollamaModelAdminClient,
    IOllamaWarmupState ollamaWarmupState,
    ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxPdfUploadBytes = 25 * 1024 * 1024;
    private static readonly IReadOnlyCollection<ModelProfile> RecommendedModels =
    [
        new("deepseek-r1:1.5b", "DeepSeek R1 1.5B", true, false, true),
        new("gemma2:2b", "Gemma 2 2B", true, false, false),
        new("phi3:mini", "Phi-3 Mini", true, false, false),
        new("llama3.2:1b", "Llama 3.2 1B", true, false, true),
        new("llama3.2:3b", "Llama 3.2 3B", true, false, true)
    ];

    [HttpGet("tools")]
    public ActionResult<IReadOnlyCollection<ChatToolDefinition>> GetTools()
        => Ok(chatToolService.GetAvailableTools());

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
            string? usedToolId = null;

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
                    usedToolId = chunk.UsedToolId;
                }
            }

            await WriteSseEventAsync(Response, "done", new { metrics, model, usedToolId }, cancellationToken);
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

    [HttpPost("stream-with-pdf")]
    [RequestSizeLimit(MaxPdfUploadBytes)]
    public async Task<IActionResult> StreamWithPdfAsync(
        [FromForm] string message,
        [FromForm] string? provider,
        [FromForm] string? model,
        [FromForm] IFormFile? pdfFile,
        [FromForm] string? contextImagesJson,
        [FromForm] string? enabledToolIdsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(new { error = "Message is required." });
        }

        if (pdfFile is null || pdfFile.Length == 0)
        {
            return BadRequest(new { error = "PDF file is required." });
        }

        var extraction = await TryExtractPdfContextAsync(pdfFile, provider, model, cancellationToken);
        if (!extraction.IsSuccess)
        {
            return StatusCode(extraction.StatusCode, new { error = extraction.ErrorMessage });
        }

        IReadOnlyCollection<ChatRequestImage>? contextImages = null;
        if (!string.IsNullOrWhiteSpace(contextImagesJson))
        {
            try
            {
                contextImages = JsonSerializer.Deserialize<IReadOnlyCollection<ChatRequestImage>>(contextImagesJson);
            }
            catch (JsonException)
            {
                return BadRequest(new { error = "Invalid contextImagesJson payload." });
            }
        }

        IReadOnlyCollection<string>? enabledToolIds = null;
        if (!string.IsNullOrWhiteSpace(enabledToolIdsJson))
        {
            try
            {
                enabledToolIds = JsonSerializer.Deserialize<IReadOnlyCollection<string>>(enabledToolIdsJson);
            }
            catch (JsonException)
            {
                return BadRequest(new { error = "Invalid enabledToolIdsJson payload." });
            }
        }

        var request = new ChatRequest(
            Message: message,
            Provider: provider,
            Model: model,
            EnabledToolIds: enabledToolIds,
            ContextText: extraction.ContextText,
            ContextFileName: pdfFile.FileName,
            ContextImages: contextImages);

        if (TryBuildWarmupBlockResponse(request, out var warmupBlock))
        {
            return warmupBlock;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            ChatMetrics? metrics = null;
            string? resolvedModel = null;
            string? usedToolId = null;

            await foreach (var chunk in chatService.StreamAsync(request, cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    await WriteSseEventAsync(Response, "chunk", new { content = chunk.Content }, cancellationToken);
                }

                if (chunk.IsDone)
                {
                    metrics = chunk.Metrics;
                    resolvedModel = chunk.Model;
                    usedToolId = chunk.UsedToolId;
                }
            }

            await WriteSseEventAsync(Response, "done", new { metrics, model = resolvedModel, usedToolId }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Chat stream with PDF canceled by client.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Streaming chat with PDF failed.");
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
                supportsTools = installedByName.TryGetValue(model.Name, out var installedToolModel)
                    ? installedToolModel.Capabilities.Contains("tools", StringComparer.OrdinalIgnoreCase)
                    : model.SupportsTools,
                capabilitySource = installedByName.ContainsKey(model.Name) ? "runtime" : "fallback",
                isInstalled = installedByName.ContainsKey(model.Name),
                isRecommended = true
            })
            .Where(model => model.supportsText);

            var customInstalled = installed
                .Where(installedModel =>
                    installedModel.SupportsText &&
                    RecommendedModels.All(model => !string.Equals(model.Name, installedModel.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(installedModel => new
                {
                    model = installedModel.Name,
                    label = installedModel.Name,
                    supportsText = installedModel.SupportsText,
                    supportsImage = installedModel.SupportsImage,
                    supportsTools = installedModel.Capabilities.Contains("tools", StringComparer.OrdinalIgnoreCase),
                    capabilitySource = "runtime",
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

    [HttpPost("context/pdf")]
    [RequestSizeLimit(MaxPdfUploadBytes)]
    public async Task<IActionResult> ExtractPdfContextAsync(
        [FromForm] IFormFile? file,
        [FromForm] string? providerHint,
        [FromForm] string? modelHint,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "PDF file is required." });
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only PDF files are supported for this endpoint." });
        }

        if (file.Length > MaxPdfUploadBytes)
        {
            return BadRequest(new { error = $"PDF is too large ({file.Length} bytes). Limit is {MaxPdfUploadBytes} bytes." });
        }

        var extraction = await TryExtractPdfContextAsync(file, providerHint, modelHint, cancellationToken);
        if (!extraction.IsSuccess)
        {
            return StatusCode(extraction.StatusCode, new { error = extraction.ErrorMessage });
        }

        return Ok(new
        {
            contextText = extraction.ContextText,
            source = "embedded-text",
            pageCount = extraction.PageCount,
            extractedChars = extraction.ContextText!.Length,
            warnings = extraction.Warnings
        });
    }

    private async Task<PdfExtractionResult> TryExtractPdfContextAsync(
        IFormFile file,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return PdfExtractionResult.Fail(StatusCodes.Status400BadRequest, "Only PDF files are supported for this endpoint.");
        }

        if (file.Length > MaxPdfUploadBytes)
        {
            return PdfExtractionResult.Fail(
                StatusCodes.Status400BadRequest,
                $"PDF is too large ({file.Length} bytes). Limit is {MaxPdfUploadBytes} bytes.");
        }

        try
        {
            await using var input = file.OpenReadStream();
            using var memory = new MemoryStream(capacity: (int)Math.Min(file.Length, MaxPdfUploadBytes));
            await input.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            using var document = PdfDocument.Open(memory);
            var pageCount = document.NumberOfPages;
            var segments = new List<string>(capacity: pageCount);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = page.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
            }

            var extractedText = string.Join("\n\n", segments).Trim();
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return PdfExtractionResult.Fail(
                    StatusCodes.Status422UnprocessableEntity,
                    "No readable embedded text was found in this PDF. OCR fallback is required for scanned PDFs.");
            }

            var warnings = new List<string>();
            extractedText = await FitTextWithinBudgetAsync(extractedText, provider, model, warnings, cancellationToken);

            return PdfExtractionResult.Success(extractedText, pageCount, warnings);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PDF extraction failed for file {FileName}.", file.FileName);
            return PdfExtractionResult.Fail(
                StatusCodes.Status400BadRequest,
                "Unable to extract text from this PDF. Ensure it is a valid text-based PDF.");
        }
    }

    private async Task<string> FitTextWithinBudgetAsync(
        string text,
        string? provider,
        string? model,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var maxContextTokens = contextWindowBudgetResolver.ResolveMaxContextTokens(provider, model);
        var tokenCount = await contextTokenCounter.CountTextTokensAsync(text, provider, model, cancellationToken);

        if (tokenCount.HasValue)
        {
            if (tokenCount.Value <= maxContextTokens)
            {
                return text;
            }

            var trimmed = await TrimToTokenBudgetAsync(text, provider, model, maxContextTokens, cancellationToken);
            warnings.Add($"Extracted text was truncated to fit {maxContextTokens} tokens for the selected provider/model.");
            return trimmed;
        }

        var maxContextCharacters = contextWindowBudgetResolver.ResolveMaxContextCharacters(provider, model);
        if (text.Length <= maxContextCharacters)
        {
            return text;
        }

        warnings.Add($"Token counting was unavailable. Extracted text was truncated to {maxContextCharacters} characters for the selected provider/model.");
        return text[..maxContextCharacters];
    }

    private async Task<string> TrimToTokenBudgetAsync(
        string text,
        string? provider,
        string? model,
        int maxContextTokens,
        CancellationToken cancellationToken)
    {
        var low = 0;
        var high = text.Length;
        var best = 0;

        while (low <= high)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mid = low + ((high - low) / 2);
            var candidate = text[..mid];
            var tokens = await contextTokenCounter.CountTextTokensAsync(candidate, provider, model, cancellationToken);

            if (!tokens.HasValue)
            {
                var maxChars = contextWindowBudgetResolver.ResolveMaxContextCharacters(provider, model);
                return text[..Math.Min(text.Length, maxChars)];
            }

            if (tokens.Value <= maxContextTokens)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (best == text.Length)
        {
            return text;
        }

        if (best <= 0)
        {
            return string.Empty;
        }

        return text[..best];
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

    private sealed record PdfExtractionResult(bool IsSuccess, string? ContextText, int PageCount, IReadOnlyCollection<string> Warnings, int StatusCode, string? ErrorMessage)
    {
        public static PdfExtractionResult Success(string contextText, int pageCount, IReadOnlyCollection<string> warnings) =>
            new(true, contextText, pageCount, warnings, StatusCodes.Status200OK, null);

        public static PdfExtractionResult Fail(int statusCode, string errorMessage) =>
            new(false, null, 0, Array.Empty<string>(), statusCode, errorMessage);
    }

    private sealed record ModelProfile(string Name, string Label, bool SupportsText, bool SupportsImage, bool SupportsTools);
}
