using System.Globalization;
using System.Text.RegularExpressions;
using Chatbot.Application.Chat;

namespace Chatbot.Application.Tools.Deterministic;

public sealed class CalculatorTool : IDeterministicChatTool
{
    private static readonly Regex AllowedExpressionRegex =
        new(@"^[0-9+\-*/%().\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string ToolId => "calculator";

    public ChatToolDefinition Definition => new(
        ToolId,
        "Calculator",
        "Evaluates basic arithmetic expressions with +, -, *, /, %, and parentheses.",
        ["calculate 12 * (3 + 4)", "calc 144 / 12", "= (9 + 1) * 5"],
        Category: "Math");

    public Task<ChatToolExecutionResult?> TryExecuteAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message) || !TryExtractExpression(message.Trim(), out var expression))
        {
            return Task.FromResult<ChatToolExecutionResult?>(null);
        }

        try
        {
            var value = ArithmeticExpressionEvaluator.Evaluate(expression);
            return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult(
                ToolId,
                $"Result: {value.ToString("0.################", CultureInfo.InvariantCulture)}"));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult<ChatToolExecutionResult?>(new ChatToolExecutionResult(
                ToolId,
                $"Calculator error: {ex.Message}"));
        }
    }

    private static bool TryExtractExpression(string message, out string expression)
    {
        expression = string.Empty;
        var normalized = message;

        if (normalized.StartsWith("=", StringComparison.Ordinal))
        {
            expression = normalized[1..].Trim();
            return IsExpressionCandidate(expression);
        }

        if (normalized.StartsWith("calc ", StringComparison.OrdinalIgnoreCase))
        {
            expression = normalized[5..].Trim();
            return IsExpressionCandidate(expression);
        }

        if (normalized.StartsWith("calculate ", StringComparison.OrdinalIgnoreCase))
        {
            expression = normalized[10..].Trim();
            return IsExpressionCandidate(expression);
        }

        if (normalized.StartsWith("what is ", StringComparison.OrdinalIgnoreCase))
        {
            expression = NormalizeWordOperators(normalized[8..].Trim().TrimEnd('?', '.'));
            return IsExpressionCandidate(expression);
        }

        normalized = NormalizeWordOperators(normalized);

        if (IsExpressionCandidate(normalized))
        {
            expression = normalized;
            return true;
        }

        return false;
    }

    private static string NormalizeWordOperators(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return expression;
        }

        var normalized = expression.Trim();

        normalized = Regex.Replace(normalized, @"\bmultiplied\s+by\b", "*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\bdivided\s+by\b", "/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\btimes\b", "*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\bplus\b", "+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\bminus\b", "-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\bmod(?:ulo)?\b", "%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

        return normalized;
    }

    private static bool IsExpressionCandidate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        if (!AllowedExpressionRegex.IsMatch(expression))
        {
            return false;
        }

        return expression.IndexOfAny(['+', '-', '*', '/', '%']) >= 0;
    }

    private static class ArithmeticExpressionEvaluator
    {
        public static decimal Evaluate(string expression)
        {
            var values = new Stack<decimal>();
            var operators = new Stack<char>();

            var i = 0;
            while (i < expression.Length)
            {
                var c = expression[i];

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '(')
                {
                    operators.Push(c);
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    while (operators.Count > 0 && operators.Peek() != '(')
                    {
                        ApplyTopOperator(values, operators);
                    }

                    if (operators.Count == 0 || operators.Pop() != '(')
                    {
                        throw new ArgumentException("Mismatched parentheses.");
                    }

                    i++;
                    continue;
                }

                if (IsOperator(c))
                {
                    var isUnaryMinus = c == '-' && (i == 0 || PreviousNonWhitespaceIsOperatorOrOpenParen(expression, i));
                    if (isUnaryMinus)
                    {
                        i++;

                        while (i < expression.Length && char.IsWhiteSpace(expression[i]))
                        {
                            i++;
                        }

                        var parsedNumber = ParseNumber(expression, ref i, isNegative: true);
                        if (!parsedNumber.HasValue)
                        {
                            throw new ArgumentException("Invalid unary minus usage.");
                        }

                        values.Push(parsedNumber.Value);
                        continue;
                    }

                    while (operators.Count > 0 && operators.Peek() != '(' &&
                           Precedence(operators.Peek()) >= Precedence(c))
                    {
                        ApplyTopOperator(values, operators);
                    }

                    operators.Push(c);
                    i++;
                    continue;
                }

                var number = ParseNumber(expression, ref i, isNegative: false);
                if (!number.HasValue)
                {
                    throw new ArgumentException("Invalid expression format.");
                }

                values.Push(number.Value);
            }

            while (operators.Count > 0)
            {
                if (operators.Peek() == '(')
                {
                    throw new ArgumentException("Mismatched parentheses.");
                }

                ApplyTopOperator(values, operators);
            }

            if (values.Count != 1)
            {
                throw new ArgumentException("Invalid expression format.");
            }

            return values.Pop();
        }

        private static decimal? ParseNumber(string expression, ref int index, bool isNegative)
        {
            var start = index;
            var hasDecimalPoint = false;

            while (index < expression.Length)
            {
                var c = expression[index];
                if (char.IsDigit(c))
                {
                    index++;
                    continue;
                }

                if (c == '.')
                {
                    if (hasDecimalPoint)
                    {
                        return null;
                    }

                    hasDecimalPoint = true;
                    index++;
                    continue;
                }

                break;
            }

            if (start == index)
            {
                return null;
            }

            var span = expression[start..index];
            if (!decimal.TryParse(span, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            return isNegative ? -number : number;
        }

        private static bool PreviousNonWhitespaceIsOperatorOrOpenParen(string expression, int index)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                var c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                return c == '(' || IsOperator(c);
            }

            return true;
        }

        private static bool IsOperator(char c)
            => c is '+' or '-' or '*' or '/' or '%';

        private static int Precedence(char c)
            => c is '*' or '/' or '%' ? 2 : 1;

        private static void ApplyTopOperator(Stack<decimal> values, Stack<char> operators)
        {
            if (operators.Count == 0)
            {
                throw new ArgumentException("Invalid expression format.");
            }

            if (values.Count < 2)
            {
                throw new ArgumentException("Invalid expression format.");
            }

            var right = values.Pop();
            var left = values.Pop();
            var op = operators.Pop();

            var result = op switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' when right == 0 => throw new ArgumentException("Division by zero is not allowed."),
                '/' => left / right,
                '%' when right == 0 => throw new ArgumentException("Division by zero is not allowed."),
                '%' => left % right,
                _ => throw new ArgumentException("Unsupported operator.")
            };

            values.Push(result);
        }
    }
}
