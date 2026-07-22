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
}
