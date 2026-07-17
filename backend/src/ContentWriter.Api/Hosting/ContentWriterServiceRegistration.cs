using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.JsonLd;
using ContentWriter.Application.Services.Publish;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.Data;
using ContentWriter.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ContentWriter.Api.Hosting;

public static class ContentWriterServiceRegistration
{
    public static IServiceCollection AddContentWriter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionResolver.Resolve(configuration);

        services.AddSingleton(new ContentWriterDatabaseOptions(connectionString));

        services.AddDbContext<ContentWriterDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    ContentWriterDbContextOptionsExtensions.MigrationsHistoryTableName,
                    ContentWriterDbContextOptionsExtensions.SchemaName));
        });

        services.AddScoped<IProjectRepository, ProjectRepository>();

        services.Configure<LlmProvidersOptions>(configuration.GetSection(LlmProvidersOptions.SectionName));
        services.Configure<CompanyProfileOptions>(configuration.GetSection(CompanyProfileOptions.SectionName));
        services.Configure<GeekBackendOptions>(configuration.GetSection(GeekBackendOptions.SectionName));

        services.AddHttpClient<LmStudioProvider>();
        services.AddHttpClient<OpenAiProvider>();
        services.AddHttpClient<AnthropicProvider>();

        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.LmStudio,
            (sp, _) => sp.GetRequiredService<LmStudioProvider>());
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.OpenAi,
            (sp, _) => sp.GetRequiredService<OpenAiProvider>());
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.Anthropic,
            (sp, _) => sp.GetRequiredService<AnthropicProvider>());

        services.AddScoped<IContentProviderFactory, ContentProviderFactory>();
        services.AddHttpClient<ISiteCrawlerService, SiteCrawlerService>();
        services.AddScoped<IKeywordHtmlParserService, KeywordHtmlParserService>();
        services.AddScoped<IContentPromptBuilder, ContentPromptBuilder>();
        services.AddScoped<ISoftwareApplicationSchemaBuilder, SoftwareApplicationSchemaBuilder>();
        services.AddScoped<ITechnicalArticleSchemaBuilder, TechnicalArticleSchemaBuilder>();
        services.AddScoped<IBlogPostingSchemaBuilder, BlogPostingSchemaBuilder>();
        services.AddScoped<IToolPageGenerator, ToolPageGenerator>();
        services.AddScoped<IContentGenerationOrchestrator, ContentGenerationOrchestrator>();
        services.AddHttpClient<IGeekBackendClient, GeekBackendClient>();
        services.AddScoped<IGeekBlogPublishService, GeekBlogPublishService>();
        services.AddSingleton<IJsonLdParserService, JsonLdParserService>();

        return services;
    }

    public static async Task InitializeContentWriterDatabaseAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ContentWriter.Startup");
        logger.LogInformation("PostgreSQL schema: {Schema}", ContentWriterDbContextOptionsExtensions.SchemaName);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentWriterDbContext>();
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Content Writer database ready.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content Writer database initialization failed.");
            if (!app.Environment.IsDevelopment())
                throw;
        }
    }
}

internal sealed record ContentWriterDatabaseOptions(string ConnectionString);
