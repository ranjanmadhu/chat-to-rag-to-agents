namespace Chatbot.Domain;

public sealed record ChatImageAttachment(
    string MimeType,
    string Base64Data,
    string? FileName = null);