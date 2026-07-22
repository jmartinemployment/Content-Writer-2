namespace ContentWriter.Application.Providers;

public enum ChatRole
{
    System,
    User,
    Assistant
}

public record ChatMessage(ChatRole Role, string Content)
{
    public string RoleString => Role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(Role))
    };
}

/// <summary>
/// <paramref name="JsonSchemaName"/>/<paramref name="JsonSchema"/> request provider-native
/// structured-output enforcement (Anthropic forced tool-use, OpenAI response_format json_schema).
/// Providers without reliable native enforcement (Groq, LM Studio) ignore these two fields and fall
/// back to prompt-only generation — correctness for those is guaranteed by the caller's own
/// two-tier validation (schema deserialization + content-hygiene scan) after the fact, not by the
/// provider. See the design plan's "provider reality check."
/// </summary>
public record ChatCompletionRequest(
    List<ChatMessage> Messages,
    double Temperature = 0.7,
    int MaxOutputTokens = 4096,
    string? Model = null,
    string? JsonSchemaName = null,
    string? JsonSchema = null);

public record ChatCompletionResult(
    string Content,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens,
    int RetryCount = 0,
    string? RetryReason = null);

public class ContentGenerationException : Exception
{
    public ContentGenerationException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public record LmStudioHealthStatus(bool IsReachable, string? ModelId, string? Message);
