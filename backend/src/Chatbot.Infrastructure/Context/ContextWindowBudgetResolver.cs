using Chatbot.Application.Chat;
using Microsoft.Extensions.Options;

namespace Chatbot.Infrastructure.Context;

public sealed class ContextWindowBudgetResolver(IOptions<ContextWindowingOptions> options) : IContextWindowBudgetResolver
{
    private readonly ContextWindowingOptions contextOptions = options.Value;

    public int ResolveMaxContextTokens(string? provider, string? model)
    {
        var normalizedProvider = Normalize(provider);
        var normalizedModel = Normalize(model);

        var maxTokens = ResolveMaxTokens(normalizedProvider, normalizedModel);
        var safetyRatio = Math.Clamp(contextOptions.SafetyRatio, 0.1, 1.0);
        var reservedOutputTokens = Math.Max(0, contextOptions.ReservedOutputTokens);

        var usableTokens = (int)Math.Floor(maxTokens * safetyRatio) - reservedOutputTokens;
        if (usableTokens < 1)
        {
            usableTokens = 1;
        }

        return usableTokens;
    }

    public int ResolveMaxContextCharacters(string? provider, string? model)
    {
        var approxCharsPerToken = Math.Max(1, contextOptions.ApproxCharsPerToken);
        var usableTokens = ResolveMaxContextTokens(provider, model);

        var dynamicChars = usableTokens * approxCharsPerToken;
        var minChars = Math.Max(1, contextOptions.MinContextChars);
        var maxChars = Math.Max(minChars, contextOptions.GlobalMaxContextChars);

        return Math.Clamp(dynamicChars, minChars, maxChars);
    }

    private int ResolveMaxTokens(string provider, string model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            var exact = contextOptions.ModelLimits.FirstOrDefault(entry =>
                string.Equals(entry.Model, model, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Normalize(entry.Provider), provider, StringComparison.Ordinal));

            if (exact is not null && exact.MaxContextTokens > 0)
            {
                return exact.MaxContextTokens;
            }

            var modelOnly = contextOptions.ModelLimits.FirstOrDefault(entry =>
                string.Equals(entry.Model, model, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(entry.Provider));

            if (modelOnly is not null && modelOnly.MaxContextTokens > 0)
            {
                return modelOnly.MaxContextTokens;
            }
        }

        return Math.Max(1024, contextOptions.DefaultMaxContextTokens);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
