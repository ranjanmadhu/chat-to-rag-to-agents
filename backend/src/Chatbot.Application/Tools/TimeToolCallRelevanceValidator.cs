namespace Chatbot.Application.Tools;

public sealed class TimeToolCallRelevanceValidator : IToolCallRelevanceValidator
{
    public string ToolId => "time";

    public ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        AiToolDefinition definition)
    {
        var normalizedMessage = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedMessage.Contains("time") || normalizedMessage.Contains("clock")
            ? new ToolCallValidationResult(true, "Time tool relevance passed.")
            : new ToolCallValidationResult(false, "Rejected: time tool was not relevant to the user request.");
    }
}
