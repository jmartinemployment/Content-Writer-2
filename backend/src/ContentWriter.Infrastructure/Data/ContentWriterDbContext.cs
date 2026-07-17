using ContentWriter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentWriter.Infrastructure.Data;

public class ContentWriterDbContext : DbContext
{
    // ASCII Unit Separator: joins/splits List<string> properties into a single nvarchar(max) column.
    // Not expected to occur naturally in scraped HTML/text content.
    private static readonly char[] ListSeparator = { (char)0x1F };

    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList());

    public ContentWriterDbContext(DbContextOptions<ContentWriterDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<CrawledSite> CrawledSites => Set<CrawledSite>();
    public DbSet<KeywordSource> KeywordSources => Set<KeywordSource>();
    public DbSet<GeneratedContent> GeneratedContents => Set<GeneratedContent>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<PublishTarget> PublishTargets => Set<PublishTarget>();

    private static string JoinList(List<string> values) => string.Join(ListSeparator[0], values);

    private static List<string> SplitList(string value) =>
        value.Length == 0 ? new List<string>() : value.Split(ListSeparator, StringSplitOptions.None).ToList();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ContentWriterDbContextOptionsExtensions.SchemaName);

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(256).IsRequired();

            entity.HasOne(c => c.PublishTarget)
                  .WithOne(t => t.Client)
                  .HasForeignKey<PublishTarget>(t => t.ClientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublishTarget>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.GeekBackendApiBaseUrl).HasMaxLength(2048).IsRequired();
            entity.Property(t => t.ApiKeyEnvVar).HasMaxLength(256).IsRequired();
            entity.HasIndex(t => t.ClientId).IsUnique();
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(256).IsRequired();
            entity.Property(p => p.ProjectUrl).HasMaxLength(2048).IsRequired();
            entity.Property(p => p.TargetKeyword).HasMaxLength(256);

            entity.HasOne(p => p.Client)
                  .WithMany(c => c.Projects)
                  .HasForeignKey(p => p.ClientId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(p => p.CrawledSite)
                  .WithOne(c => c.Project)
                  .HasForeignKey<CrawledSite>(c => c.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.KeywordSources)
                  .WithOne(k => k.Project)
                  .HasForeignKey(k => k.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.GeneratedContents)
                  .WithOne(g => g.Project)
                  .HasForeignKey(g => g.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CrawledSite>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.JsonLdBlocks)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
            entity.Property(c => c.Headings)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
            entity.Property(c => c.Paragraphs)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
        });

        modelBuilder.Entity<KeywordSource>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.RawContent);
            entity.Property(k => k.ExtractedHeadings)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
            entity.Property(k => k.ExtractedParagraphs)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
            entity.Property(k => k.ExtractedQuestions)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
        });

        modelBuilder.Entity<GeneratedContent>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Title).HasMaxLength(512);
            entity.Property(g => g.Slug).HasMaxLength(512);
            entity.Property(g => g.BodyHtml);
            entity.Property(g => g.JsonLdSchema);
            entity.Property(g => g.MetaDescription);
            entity.Property(g => g.Keywords)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
            entity.Property(g => g.SectionOutline)
                  .HasConversion(v => JoinList(v), v => SplitList(v), StringListComparer);
        });
    }
}
