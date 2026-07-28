using System.Net;

namespace Chatbot.Infrastructure;

internal static class ProviderErrorMessages
{
    public static string BuildGeminiStatusMessage(HttpStatusCode? statusCode)
    {
        if (statusCode is HttpStatusCode.TooManyRequests)
        {
            return "Gemini says 429: we have burned through Madhu's credits for now. Give it a short break and try again soon, or check quota/billing.";
        }

        return $"Gemini request failed ({statusCode}). Check API key permissions, model availability, and Gemini quota/rate limits for this project.";
    }

    public static string BuildOllamaModelNotFoundMessage(string model)
    {
        return $"Ollama cannot find model '{model}' right now. The model shelf looks empty. Run `ollama pull {model}` or point appsettings to a model you already have.";
    }

    public static string BuildOllamaTimeoutMessage(int timeoutSeconds)
    {
        return $"Ollama is still thinking and hit the timeout wall ({timeoutSeconds}s). Try a smaller model, ask for a shorter answer, or increase Ollama:RequestTimeoutSeconds in appsettings.";
    }

    public static string BuildOllamaUnavailableMessage()
    {
        return "Ollama looks off duty at the moment. Start it with `ollama serve`, pull the configured model, then ask again.";
    }
}