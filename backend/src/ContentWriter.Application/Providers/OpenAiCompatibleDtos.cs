using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ContentWriter.Application.Providers;

/// <summary>
/// Shared wire format for any backend that speaks the OpenAI /v1/chat/completions
/// dialect - used by both LmStudioProvider and OpenAiProvider.
/// </summary>
internal sealed class OpenAiCompatibleRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OpenAiCompatibleMessage> Messages { get; set; } = new();
    [JsonPropertyName("temperature")] public double Temperature { get; set; }
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; } = false;

    /// <summary>Only set when the caller supplied a JsonSchema — omitted (null) otherwise, which
    /// is a no-op on every OpenAI-compatible backend (unchanged behavior when no schema is requested).</summary>
    [JsonPropertyName("response_format")] public OpenAiResponseFormat? ResponseFormat { get; set; }
}

/// <summary>Provider-native structured-output enforcement (OpenAI/Groq's shared
/// response_format: json_schema contract). Not honored by LmStudioProvider today.</summary>
internal sealed class OpenAiResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; set; } = "json_schema";
    [JsonPropertyName("json_schema")] public OpenAiJsonSchemaSpec JsonSchema { get; set; } = new();
}

internal sealed class OpenAiJsonSchemaSpec
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>OpenAI's gpt-4o (2024-08-06+) supports strict:true (grammar-constrained, guaranteed
    /// schema compliance). Groq's structured outputs only support strict:true on specific models
    /// (openai/gpt-oss-20b/120b) — not the llama-3.3-70b-versatile this project is configured for —
    /// so GroqProvider sets this to false (best-effort: schema is a strong hint, not guaranteed).</summary>
    [JsonPropertyName("strict")] public bool Strict { get; set; } = true;

    [JsonPropertyName("schema")] public JsonNode? Schema { get; set; }
}

internal sealed record OpenAiCompatibleMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed class OpenAiCompatibleResponse
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("choices")] public List<OpenAiChoice> Choices { get; set; } = new();
    [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiCompatibleMessage Message { get; set; } = new("assistant", string.Empty);
    // #region agent log
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    // #endregion
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("prompt_tokens_details")] public OpenAiPromptTokensDetails? PromptTokensDetails { get; set; }
}

internal sealed class OpenAiPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")] public int CachedTokens { get; set; }
}
