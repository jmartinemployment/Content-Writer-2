namespace ContentWriter.Application.Providers;

/// <summary>
/// Global cap on concurrent LLM calls across every project pipeline and the editorial reviewer
/// model — separate from the per-project concurrency cap, since review passes stack additional
/// calls on top of generation. See standing policy in the v2 plan: a per-project cap alone
/// undercounts once review passes stack on top.
/// </summary>
public sealed class LlmConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public LlmConcurrencyGate(int maxConcurrentCalls)
    {
        _semaphore = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>Wraps any <see cref="IContentGenerationProvider"/> so every call is gated by the global LLM concurrency limit.</summary>
public sealed class ConcurrencyLimitingContentGenerationProvider : IContentGenerationProvider
{
    private readonly IContentGenerationProvider _inner;
    private readonly LlmConcurrencyGate _gate;

    public ConcurrencyLimitingContentGenerationProvider(IContentGenerationProvider inner, LlmConcurrencyGate gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public Domain.Enums.LlmProviderType ProviderType => _inner.ProviderType;

    public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => _inner.CompleteAsync(request, cancellationToken), cancellationToken);
}
