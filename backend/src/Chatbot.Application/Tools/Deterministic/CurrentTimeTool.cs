using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools.Deterministic;

public sealed class CurrentTimeTool : IDeterministicChatTool
{
    public string ToolId => "time";

    public ChatToolDefinition Definition => new(
        ToolId,
        "Current Time",
        "Returns the current UTC time.",
        ["what time is it", "current time"],
        Category: "Date & Time");

    public Task<ChatToolExecutionResult?> TryExecuteAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult<ChatToolExecutionResult?>(null);
        }

        var normalized = message.Trim().ToLowerInvariant();
        if (!normalized.Contains("time") && !normalized.Contains("clock"))
        {
            return Task.FromResult<ChatToolExecutionResult?>(null);
        }

        var utcNow = DateTimeOffset.UtcNow;
        return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult(
            ToolId,
            $"Current UTC time: {utcNow:HH:mm:ss} (UTC)"));
    }
}
