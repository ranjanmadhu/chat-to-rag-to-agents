using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Chatbot.Application.Tools;

public sealed class SystemToolsPlugin
{
    [KernelFunction("calculator")]
    [Description("Evaluates a basic arithmetic expression with +, -, *, /, %, and parentheses.")]
    public string Calculator(
        [Description("Arithmetic expression to evaluate, for example: 12 * (3 + 4)")] string expression)
        => expression;

    [KernelFunction("date")]
    [Description("Returns the current UTC date.")]
    public string CurrentDate()
        => "";

    [KernelFunction("time")]
    [Description("Returns the current UTC time.")]
    public string CurrentTime()
        => "";
}
