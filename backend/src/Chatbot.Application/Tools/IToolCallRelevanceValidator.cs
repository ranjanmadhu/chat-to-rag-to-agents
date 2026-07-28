namespace Chatbot.Application.Tools;

public interface IToolCallRelevanceValidator
{
    string ToolId { get; }

    ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        AiToolDefinition definition);
}
