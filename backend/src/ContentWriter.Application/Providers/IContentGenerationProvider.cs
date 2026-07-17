using ContentWriter.Domain.Enums;

namespace ContentWriter.Application.Providers;

/// <summary>
/// Common contract every LLM backend (LM Studio, OpenAI, Anthropic) implements so the
/// orchestrator never needs to know which vendor is serving a given request.
/// </summary>
public interface IContentGenerationProvider
{
    LlmProviderType ProviderType { get; }

    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
