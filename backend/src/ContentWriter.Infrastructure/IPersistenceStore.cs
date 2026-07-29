namespace ContentWriter.Infrastructure;

/// <summary>
/// Durable storage adapter for serialized aggregates (Project, Client).
/// Implementations (filesystem, GeekRepository, GitHub) are swappable behind this interface.
/// </summary>
public interface IPersistenceStore
{
    /// <summary>Persist a JSON document. Overwrites if the id already exists (upsert).</summary>
    Task SaveDocumentAsync(string collection, Guid id, string json, CancellationToken cancellationToken = default);

    /// <summary>Retrieve a persisted JSON document by id. Returns null if not found.</summary>
    Task<string?> LoadDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default);

    /// <summary>List all document ids in a collection. Empty list if collection doesn't exist.</summary>
    Task<IReadOnlyList<Guid>> ListDocumentsAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Delete a persisted document. No-op if id doesn't exist.</summary>
    Task DeleteDocumentAsync(string collection, Guid id, CancellationToken cancellationToken = default);
}
