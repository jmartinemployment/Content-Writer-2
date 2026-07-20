namespace ContentWriter.Application.Providers;

public class LlmProvidersOptions
{
    public const string SectionName = "LlmProviders";

    public LmStudioOptions LmStudio { get; set; } = new();
    public OpenAiOptions OpenAi { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public GroqOptions Groq { get; set; } = new();

    /// <summary>Which provider services requests when a caller doesn't specify one explicitly.</summary>
    public string DefaultProvider { get; set; } = "LmStudio";
}

public class LmStudioOptions
{
    public string BaseUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
    public string Model { get; set; } = "local-model";
    public int TimeoutSeconds { get; set; } = 900;
}

public class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int TimeoutSeconds { get; set; } = 120;
}

public class AnthropicOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-5";
    public string AnthropicVersion { get; set; } = "2023-06-01";
    public int TimeoutSeconds { get; set; } = 120;
}

public class GroqOptions
{
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public int TimeoutSeconds { get; set; } = 120;
}
