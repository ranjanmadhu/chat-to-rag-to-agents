using System.Text.Json;
using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools;

public sealed class SemanticKernelAiToolService(IChatToolService deterministicTools) : IAiToolService
{
    private static readonly IReadOnlyCollection<AiToolDefinition> ToolDefinitions =
    [
        new(
            "calculator",
            "Evaluates a basic arithmetic expression with +, -, *, /, %, and parentheses.",
            [
                new AiToolParameterDefinition(
                    "expression",
                    "string",
                    "Arithmetic expression to evaluate, for example: 12 * (3 + 4)",
                    IsRequired: true)
            ]),
        new(
            "date",
            "Returns the current UTC date.",
            []),
        new(
            "time",
            "Returns the current UTC time.",
            [])
    ];

    public IReadOnlyCollection<AiToolDefinition> GetEnabledToolDefinitions(IReadOnlyCollection<string>? enabledToolIds)
    {
        if (enabledToolIds is null || enabledToolIds.Count == 0)
        {
            return [];
        }

        var enabled = enabledToolIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabled.Count == 0)
        {
            return [];
        }

        return ToolDefinitions
            .Where(def => enabled.Contains(def.Id))
            .ToArray();
    }

    public async Task<ChatToolExecutionResult?> TryExecuteToolCallAsync(
        AiToolCall toolCall,
        string userMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(toolCall.ToolId))
        {
            return null;
        }

        var normalizedToolId = toolCall.ToolId.Trim().ToLowerInvariant();

        return normalizedToolId switch
        {
            "calculator" => await ExecuteCalculatorAsync(toolCall.ArgumentsJson, cancellationToken),
            "date" => await deterministicTools.TryExecuteAsync(userMessage, ["date"], cancellationToken),
            "time" => await deterministicTools.TryExecuteAsync(userMessage, ["time"], cancellationToken),
            _ => null
        };
    }

    private async Task<ChatToolExecutionResult?> ExecuteCalculatorAsync(
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (!document.RootElement.TryGetProperty("expression", out var expressionElement) ||
                expressionElement.ValueKind != JsonValueKind.String)
            {
                return new ChatToolExecutionResult("calculator", "Calculator error: expression is required.");
            }

            var expression = expressionElement.GetString() ?? string.Empty;
            return await deterministicTools.TryExecuteAsync($"calc {expression}", ["calculator"], cancellationToken);
        }
        catch (JsonException)
        {
            return new ChatToolExecutionResult("calculator", "Calculator error: invalid tool arguments.");
        }
    }
}
