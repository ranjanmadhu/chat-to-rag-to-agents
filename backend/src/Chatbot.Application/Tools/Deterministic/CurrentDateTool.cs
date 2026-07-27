using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools.Deterministic;

public sealed class CurrentDateTool : IDeterministicChatTool
{
    public string ToolId => "date";

    public ChatToolDefinition Definition => new(
        ToolId,
        "Current Date",
        "Returns the current UTC date.",
        ["what is the date", "today's date"],
        Category: "Date & Time");

    public Task<ChatToolExecutionResult?> TryExecuteAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult<ChatToolExecutionResult?>(null);
        }

        var normalized = message.Trim().ToLowerInvariant();
        if (!normalized.Contains("date") && !normalized.Contains("today"))
        {
            return Task.FromResult<ChatToolExecutionResult?>(null);
        }

        var utcNow = DateTimeOffset.UtcNow;
        return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult(
            ToolId,
            $"Current UTC date: {utcNow:yyyy-MM-dd}"));
    }
}
