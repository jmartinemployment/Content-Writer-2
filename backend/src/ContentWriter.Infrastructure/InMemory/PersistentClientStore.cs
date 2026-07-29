using System.Collections.Concurrent;
using ContentWriter.Domain.Entities;
using ContentWriter.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure.InMemory;

/// <summary>
/// Write-through persistent client store. Wraps a ConcurrentDictionary cache and
/// persists snapshots to an IPersistenceStore on AddAsync/SaveAsync.
/// GetAsync returns the live cached instance for direct mutation.
/// </summary>
public sealed class PersistentClientStore : IClientStore
{
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly IPersistenceStore _persistence;
    private readonly ILogger<PersistentClientStore> _logger;
    private const string Collection = "clients";

    public PersistentClientStore(IPersistenceStore persistence, ILogger<PersistentClientStore> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public Task<List<Client>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_clients.Values.OrderBy(c => c.Name).ToList());

    public Task<Client?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_clients.GetValueOrDefault(id));

    public async Task AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        _clients[client.Id] = client;
        await SaveAsync(client, cancellationToken);
    }

    public async Task SaveAsync(Client client, CancellationToken cancellationToken = default)
    {
        var json = ClientSnapshotSerializer.Serialize(client);
        await _persistence.SaveDocumentAsync(Collection, client.Id, json, cancellationToken);
        _logger.LogDebug("Persisted client {ClientId}", client.Id);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(!_clients.IsEmpty);

    /// <summary>Load all clients from persistent storage into the cache.</summary>
    public async Task HydrateAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _persistence.ListDocumentsAsync(Collection, cancellationToken);
        _logger.LogInformation("Hydrating {Count} client(s) from persistent storage", ids.Count);

        foreach (var id in ids)
        {
            try
            {
                var json = await _persistence.LoadDocumentAsync(Collection, id, cancellationToken);
                if (string.IsNullOrEmpty(json))
                {
                    _logger.LogWarning("Client {ClientId} has empty data, skipping", id);
                    continue;
                }

                var client = ClientSnapshotSerializer.Deserialize(json);
                _clients[client.Id] = client;

                _logger.LogDebug("Hydrated client {ClientId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hydrate client {ClientId}", id);
            }
        }

        _logger.LogInformation("Hydration complete: {Count} client(s) loaded", _clients.Count);
    }
}
