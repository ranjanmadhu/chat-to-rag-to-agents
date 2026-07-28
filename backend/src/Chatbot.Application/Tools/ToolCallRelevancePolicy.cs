using System.Text.Json;

namespace Chatbot.Application.Tools;

public sealed class ToolCallRelevancePolicy(IEnumerable<IToolCallRelevanceValidator> validators) : IToolCallRelevancePolicy
{
    private readonly IReadOnlyDictionary<string, IToolCallRelevanceValidator> validatorsByToolId = validators
        .GroupBy(validator => validator.ToolId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        IReadOnlyCollection<AiToolDefinition> enabledTools)
    {
        if (string.IsNullOrWhiteSpace(toolCall.ToolId))
        {
            return new ToolCallValidationResult(false, "Rejected: model returned an empty tool id.");
        }

        var normalizedToolId = toolCall.ToolId.Trim().ToLowerInvariant();
        var definition = enabledTools.FirstOrDefault(tool =>
            string.Equals(tool.Id, normalizedToolId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return new ToolCallValidationResult(false, $"Rejected: tool '{normalizedToolId}' is not enabled.");
        }

        var argsValidation = ValidateArguments(toolCall.ArgumentsJson, definition);
        if (!argsValidation.IsValid)
        {
            return argsValidation;
        }

        var relevanceValidation = ValidateRelevance(toolCall, normalizedToolId, userMessage, definition);
        if (!relevanceValidation.IsValid)
        {
            return relevanceValidation;
        }

        return new ToolCallValidationResult(true, $"Accepted: tool '{normalizedToolId}' passed schema and relevance checks.");
    }

    private static ToolCallValidationResult ValidateArguments(string argumentsJson, AiToolDefinition definition)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new ToolCallValidationResult(false, $"Rejected: tool '{definition.Id}' arguments are not valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ToolCallValidationResult(false, $"Rejected: tool '{definition.Id}' arguments must be a JSON object.");
        }

        foreach (var parameter in definition.Parameters.Where(parameter => parameter.IsRequired))
        {
            if (!root.TryGetProperty(parameter.Name, out var value))
            {
                return new ToolCallValidationResult(false, $"Rejected: required argument '{parameter.Name}' is missing for tool '{definition.Id}'.");
            }

            if (!IsExpectedType(value, parameter.Type))
            {
                return new ToolCallValidationResult(false, $"Rejected: argument '{parameter.Name}' for tool '{definition.Id}' is not a valid {parameter.Type}.");
            }
        }

        return new ToolCallValidationResult(true, "Arguments are valid.");
    }

    private ToolCallValidationResult ValidateRelevance(
        AiToolCall toolCall,
        string toolId,
        string userMessage,
        AiToolDefinition definition)
    {
        if (!validatorsByToolId.TryGetValue(toolId, out var validator))
        {
            return new ToolCallValidationResult(true, $"No specific relevance policy for tool '{toolId}'.");
        }

        return validator.Validate(toolCall, userMessage, definition);
    }

    private static bool IsExpectedType(JsonElement value, string expectedType)
    {
        return expectedType.ToLowerInvariant() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => true
        };
    }
}
