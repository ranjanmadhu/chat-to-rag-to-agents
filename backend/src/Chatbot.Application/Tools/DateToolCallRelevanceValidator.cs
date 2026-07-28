namespace Chatbot.Application.Tools;

public sealed class DateToolCallRelevanceValidator : IToolCallRelevanceValidator
{
    public string ToolId => "date";

    public ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        AiToolDefinition definition)
    {
        var normalizedMessage = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedMessage.Contains("date") || normalizedMessage.Contains("today")
            ? new ToolCallValidationResult(true, "Date tool relevance passed.")
            : new ToolCallValidationResult(false, "Rejected: date tool was not relevant to the user request.");
    }
}
