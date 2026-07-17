using ContentWriter.Domain.Entities;

namespace ContentWriter.Infrastructure.Repositories;

public interface IProjectRepository : IRepository<Project>
{
    /// <summary>Loads a project with its crawl data, keyword sources, and generated content graph.</summary>
    Task<Project?> GetWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default);

    /// <summary>Deletes projects older than <paramref name="maxAge"/> that never reached Completed status.</summary>
    Task<int> PurgeStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    Task AddKeywordSourceAsync(KeywordSource keywordSource, CancellationToken cancellationToken = default);

    Task SetCrawledSiteAsync(CrawledSite crawledSite, CancellationToken cancellationToken = default);

    Task AddContentAsync(GeneratedContent content, CancellationToken cancellationToken = default);

    void RemoveGeneratedContents(IEnumerable<GeneratedContent> contents);

    void RemoveKeywordSource(KeywordSource keywordSource);
}
