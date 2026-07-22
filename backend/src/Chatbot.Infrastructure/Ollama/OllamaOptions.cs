namespace Chatbot.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string ChatModel { get; init; } = "gemma4";
    public int RequestTimeoutSeconds { get; init; } = 300;
}
