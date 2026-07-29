using System.Collections.Concurrent;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace ContentWriter.Infrastructure.InMemory;

/// <summary>
/// Write-through persistent project store. Wraps a ConcurrentDictionary cache and
/// persists snapshots to an IPersistenceStore on AddAsync/SaveAsync.
/// GetAsync returns the live cached instance for direct mutation.
/// </summary>
public sealed class PersistentProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<Guid, Project> _projects = new();
    private readonly IPersistenceStore _persistence;
    private readonly IClientStore _clientStore;
    private readonly ILogger<PersistentProjectStore> _logger;
    private const string Collection = "projects";

    public PersistentProjectStore(IPersistenceStore persistence, IClientStore clientStore, ILogger<PersistentProjectStore> logger)
    {
        _persistence = persistence;
        _clientStore = clientStore;
        _logger = logger;
    }

    public Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<List<Project>> ListAsync(Func<Project, bool>? predicate = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<Project> query = _projects.Values;
        if (predicate is not null)
            query = query.Where(predicate);
        return Task.FromResult(query.ToList());
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        await SaveAsync(project, cancellationToken);
    }

    public async Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        var json = ProjectSnapshotSerializer.Serialize(project);
        await _persistence.SaveDocumentAsync(Collection, project.Id, json, cancellationToken);
        _logger.LogDebug("Persisted project {ProjectId}", project.Id);
    }

    public Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Values.OrderByDescending(p => p.CreatedAtUtc).Take(take).ToList());

    public async Task<int> PurgeStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var stale = _projects.Values
            .Where(p => p.Status != ProjectStatus.Completed && p.CreatedAtUtc < cutoff)
            .ToList();

        foreach (var project in stale)
        {
            _projects.TryRemove(project.Id, out _);
            await _persistence.DeleteDocumentAsync(Collection, project.Id, cancellationToken);
        }

        return stale.Count;
    }

    /// <summary>Load all projects from persistent storage into the cache, rehydrating Client refs from the client store.</summary>
    public async Task HydrateAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _persistence.ListDocumentsAsync(Collection, cancellationToken);
        _logger.LogInformation("Hydrating {Count} project(s) from persistent storage", ids.Count);

        foreach (var id in ids)
        {
            try
            {
                var json = await _persistence.LoadDocumentAsync(Collection, id, cancellationToken);
                if (string.IsNullOrEmpty(json))
                {
                    _logger.LogWarning("Project {ProjectId} has empty data, skipping", id);
                    continue;
                }

                var project = ProjectSnapshotSerializer.Deserialize(json, null);
                var client = await _clientStore.GetAsync(project.ClientId, cancellationToken);
                project.Client = client;
                _projects[project.Id] = project;

                _logger.LogDebug("Hydrated project {ProjectId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hydrate project {ProjectId}", id);
            }
        }

        _logger.LogInformation("Hydration complete: {Count} project(s) loaded", _projects.Count);
    }
}
