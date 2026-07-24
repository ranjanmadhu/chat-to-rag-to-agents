namespace Chatbot.Application.Chat;

public interface IContextWindowBudgetResolver
{
    int ResolveMaxContextTokens(string? provider, string? model);
    int ResolveMaxContextCharacters(string? provider, string? model);
}
