namespace Chatbot.Application.Chat;

public sealed record ChatRequest(
	string Message,
	string? Provider = null,
	string? Model = null,
	string? ContextText = null,
	string? ContextFileName = null);
