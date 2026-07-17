using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Infrastructure.Data;

public static class ContentWriterDbContextOptionsExtensions
{
    public const string SchemaName = "content_writer_v2";
    public const string MigrationsHistoryTableName = "__EFContentWriterMigrationsHistory";

    public static DbContextOptionsBuilder<ContentWriterDbContext> UseContentWriterPostgres(
        this DbContextOptionsBuilder<ContentWriterDbContext> builder,
        string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTableName, SchemaName));
}
