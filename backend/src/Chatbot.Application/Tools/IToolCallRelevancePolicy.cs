namespace Chatbot.Application.Tools;

public interface IToolCallRelevancePolicy
{
    ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        IReadOnlyCollection<AiToolDefinition> enabledTools);
}

public sealed record ToolCallValidationResult(
    bool IsValid,
    string Reason);
