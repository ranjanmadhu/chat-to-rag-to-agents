namespace Chatbot.Infrastructure.Gemini;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3.6-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public int RequestTimeoutSeconds { get; set; } = 300;
}
