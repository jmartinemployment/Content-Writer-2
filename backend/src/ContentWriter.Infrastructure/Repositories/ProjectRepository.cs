using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Infrastructure.Repositories;

public class ProjectRepository : Repository<Project>, IProjectRepository
{
    public ProjectRepository(ContentWriterDbContext context) : base(context)
    {
    }

    public async Task<Project?> GetWithDetailsAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await DbSet
            .Include(p => p.Client)
                .ThenInclude(c => c!.PublishTarget)
            .Include(p => p.CrawledSite)
            .Include(p => p.KeywordSources)
            .Include(p => p.GeneratedContents)
                .ThenInclude(g => g.ReviewVerdicts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

    public async Task<List<Project>> GetRecentAsync(int take = 25, CancellationToken cancellationToken = default)
        => await DbSet
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> PurgeStaleAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var stale = await DbSet
            .Where(p => p.Status != ProjectStatus.Completed && p.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        DbSet.RemoveRange(stale);
        await Context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    public async Task AddKeywordSourceAsync(KeywordSource keywordSource, CancellationToken cancellationToken = default)
        => await Context.KeywordSources.AddAsync(keywordSource, cancellationToken);

    public async Task SetCrawledSiteAsync(CrawledSite crawledSite, CancellationToken cancellationToken = default)
    {
        var existing = await Context.CrawledSites
            .FirstOrDefaultAsync(c => c.ProjectId == crawledSite.ProjectId, cancellationToken);

        if (existing is not null)
        {
            Context.CrawledSites.Remove(existing);
        }

        await Context.CrawledSites.AddAsync(crawledSite, cancellationToken);
    }

    public async Task AddContentAsync(GeneratedContent content, CancellationToken cancellationToken = default)
        => await Context.GeneratedContents.AddAsync(content, cancellationToken);

    public void RemoveGeneratedContents(IEnumerable<GeneratedContent> contents) =>
        Context.GeneratedContents.RemoveRange(contents);

    public void RemoveKeywordSource(KeywordSource keywordSource) => Context.KeywordSources.Remove(keywordSource);
}
