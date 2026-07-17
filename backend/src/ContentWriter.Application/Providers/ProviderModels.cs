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

public record ChatCompletionRequest(
    List<ChatMessage> Messages,
    double Temperature = 0.7,
    int MaxOutputTokens = 4096,
    string? Model = null);

public record ChatCompletionResult(
    string Content,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens);

public class ContentGenerationException : Exception
{
    public ContentGenerationException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public record LmStudioHealthStatus(bool IsReachable, string? ModelId, string? Message);
