namespace Chatbot.Application.Chat;

public interface IContextTokenCounter
{
    Task<int?> CountTextTokensAsync(string text, string? provider, string? model, CancellationToken cancellationToken);
}
