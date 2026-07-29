using System.Collections.Concurrent;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Infrastructure.InMemory;

/// <summary>
/// Holds every Project (and its full object graph: CrawledSite, KeywordSources, GeneratedContents,
/// ReviewVerdicts) for the lifetime of this process.
/// </summary>
public interface IProjectStore
{
    /// <summary>Returns the live Project instance — callers mutate its navigation collections directly.</summary>
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Project>> ListAsync(Func<Project, bool>? predicate = null, CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
    Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default);

    /// <summary>Persists an existing project (after mutation). Caller must ensure the project is already in the store.</summary>
    Task SaveAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>Removes projects older than <paramref name="maxAge"/> that never reached Completed status.</summary>
    Task<int> PurgeStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}

public sealed class ProjectStore : IProjectStore
{
    private readonly ConcurrentDictionary<Guid, Project> _projects = new();

    public Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<List<Project>> ListAsync(Func<Project, bool>? predicate = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<Project> query = _projects.Values;
        if (predicate is not null)
            query = query.Where(predicate);
        return Task.FromResult(query.ToList());
    }

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _projects[project.Id] = project;
        return Task.CompletedTask;
    }

    public Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        // Base implementation: keep project in cache. Persistent stores override.
        return Task.CompletedTask;
    }

    public Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default) =>
        Task.FromResult(_projects.Values.OrderByDescending(p => p.CreatedAtUtc).Take(take).ToList());

    public Task<int> PurgeStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var stale = _projects.Values
            .Where(p => p.Status != ProjectStatus.Completed && p.CreatedAtUtc < cutoff)
            .ToList();

        foreach (var project in stale)
            _projects.TryRemove(project.Id, out _);

        return Task.FromResult(stale.Count);
    }
}
