using Chatbot.Application.Tools.Deterministic;

namespace Chatbot.Application.Chat;

public sealed class DeterministicChatToolService(IEnumerable<IDeterministicChatTool> tools) : IChatToolService
{
    private readonly IReadOnlyCollection<IDeterministicChatTool> toolList = tools.ToArray();

    public IReadOnlyCollection<ChatToolDefinition> GetAvailableTools()
        => toolList.Select(tool => tool.Definition).ToArray();

    public async Task<ChatToolExecutionResult?> TryExecuteAsync(
        string message,
        IReadOnlyCollection<string>? enabledToolIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message) || enabledToolIds is null || enabledToolIds.Count == 0)
        {
            return null;
        }

        var enabled = enabledToolIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabled.Count == 0)
        {
            return null;
        }

        foreach (var tool in toolList)
        {
            if (!enabled.Contains(tool.ToolId))
            {
                continue;
            }

            var result = await tool.TryExecuteAsync(message, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
