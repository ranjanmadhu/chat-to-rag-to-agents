namespace Chatbot.Domain;

public sealed record ChatMessage(
	string Role,
	string Content,
	IReadOnlyCollection<ChatImageAttachment>? Images = null);
