namespace Chatbot.Application.Chat;

public sealed record ChatRequest(string Message, string? Provider = null);
