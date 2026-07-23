namespace Chatbot.Application.Chat;

public interface IChatModelClientFactory
{
    IChatModelClient Resolve(string? provider);
}
