namespace ContentWriter.Infrastructure;

public record CachedToolContent(string DisplayName, string OverviewJson);

/// <summary>
/// Cross-project cache for a tool's shared, tool-intrinsic content (Overview + capabilities) —
/// lets the same real-world product (e.g. "Zapier") reuse that content across every
/// department/project that mentions it instead of regenerating it from scratch every time.
/// Department-specific framing and metadata are never cached here; callers must still generate
/// those fresh. Implementations are swappable behind this interface — GeekRepository-backed when
/// hosted in GeekAPI, a no-op default otherwise (standalone/dev use, matching
/// <see cref="IPersistenceStore"/>'s FileSystemPersistenceStore-by-default pattern).
/// </summary>
public interface IToolContentCacheStore
{
    /// <summary>Looks up cached content by tool name (any casing/formatting — implementations
    /// canonicalize internally). Returns null on a cache miss.</summary>
    Task<CachedToolContent?> GetAsync(string toolName, CancellationToken cancellationToken = default);

    /// <summary>Stores/overwrites the cached content for a tool name.</summary>
    Task SaveAsync(string toolName, string displayName, string overviewJson, CancellationToken cancellationToken = default);
}

/// <summary>No-op default — every lookup misses, every save is discarded. Used when no
/// GeekRepository-backed store is supplied (standalone/dev), so ToolPageGenerator's cache-check
/// path is always safe to call regardless of host.</summary>
public sealed class NullToolContentCacheStore : IToolContentCacheStore
{
    public Task<CachedToolContent?> GetAsync(string toolName, CancellationToken cancellationToken = default) =>
        Task.FromResult<CachedToolContent?>(null);

    public Task SaveAsync(string toolName, string displayName, string overviewJson, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
