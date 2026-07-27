namespace Chatbot.Application.Chat;

public sealed record ChatRequestImage(
    string? Base64,
    string? MimeType,
    string? FileName = null);

public sealed record ChatRequest(
	string Message,
	string? Provider = null,
	string? Model = null,
	IReadOnlyCollection<string>? EnabledToolIds = null,
	string? ContextText = null,
	string? ContextFileName = null,
	IReadOnlyCollection<ChatRequestImage>? ContextImages = null,
	string? ContextImageBase64 = null,
	string? ContextImageMimeType = null,
	string? ContextImageFileName = null);
