namespace Chatbot.Application.Chat;

public sealed record ChatResponse(string Message, string Model, ChatMetrics? Metrics = null);
