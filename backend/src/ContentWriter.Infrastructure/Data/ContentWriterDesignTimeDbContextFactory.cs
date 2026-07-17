using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentWriter.Infrastructure.Data;

public sealed class ContentWriterDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ContentWriterDbContext>
{
    public ContentWriterDbContext CreateDbContext(string[] args)
    {
        Env.TraversePath().Load();

        var raw = Environment.GetEnvironmentVariable("CONTENT_WRITER_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        var connectionString = DatabaseConnectionResolver.NormalizePostgresUrl(raw);
        var optionsBuilder = new DbContextOptionsBuilder<ContentWriterDbContext>();
        optionsBuilder.UseContentWriterPostgres(connectionString);
        return new ContentWriterDbContext(optionsBuilder.Options);
    }
}
