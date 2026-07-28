using System.Text.RegularExpressions;

namespace Chatbot.Application.Tools;

public sealed class CalculatorToolCallRelevanceValidator : IToolCallRelevanceValidator
{
    private static readonly Regex CalculatorCandidateRegex =
        new(@"[0-9\s+\-*/%().]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string ToolId => "calculator";

    public ToolCallValidationResult Validate(
        AiToolCall toolCall,
        string userMessage,
        AiToolDefinition definition)
    {
        var normalizedMessage = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        var hasCalculatorKeyword = normalizedMessage.Contains("calc") || normalizedMessage.Contains("calculate");
        var hasOperators = normalizedMessage.IndexOfAny(['+', '-', '*', '/', '%']) >= 0;
        var hasMathCandidate = CalculatorCandidateRegex.IsMatch(normalizedMessage) && hasOperators;

        return hasCalculatorKeyword || hasMathCandidate
            ? new ToolCallValidationResult(true, "Calculator relevance passed.")
            : new ToolCallValidationResult(false, "Rejected: calculator tool was not relevant to the user request.");
    }
}
