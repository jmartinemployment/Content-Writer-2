using System.Collections.Concurrent;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Infrastructure.InMemory;

/// <summary>
/// Holds every Project (and its full object graph: CrawledSite, KeywordSources, GeneratedContents,
/// ReviewVerdicts) for the lifetime of this process. No database, no persistence — content-writer-v2
/// only durably saves output by committing .mdx files to the geekatyourspot GitHub repo
/// (see GeekatyourspotCommitService). Everything here is gone on restart, by design.
/// </summary>
public interface IProjectStore
{
    /// <summary>Returns the live Project instance — callers mutate its navigation collections directly; there is no separate save step.</summary>
    Task<Project?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Project>> ListAsync(Func<Project, bool>? predicate = null, CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
    Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default);

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
