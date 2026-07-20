using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.Export;
using ContentWriter.Application.Services.JsonLd;
using ContentWriter.Application.Services.Publish;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.Review;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
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
        var connectionString = DatabaseConnectionResolver.TryResolve(configuration);

        services.AddSingleton(new ContentWriterDatabaseOptions(connectionString ?? string.Empty));

        services.AddDbContext<ContentWriterDbContext>(options =>
        {
            // No Postgres connection configured yet (e.g. no DATABASE_URL) — run on an in-memory
            // store so the app works without a hosted DB. Set DATABASE_URL (e.g. to a Supabase
            // Postgres instance) to switch back to real persistence; no other code changes needed.
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseInMemoryDatabase("ContentWriter");
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        ContentWriterDbContextOptionsExtensions.MigrationsHistoryTableName,
                        ContentWriterDbContextOptionsExtensions.SchemaName));
            }
        });

        services.AddScoped<IProjectRepository, ProjectRepository>();

        services.Configure<LlmProvidersOptions>(configuration.GetSection(LlmProvidersOptions.SectionName));
        services.Configure<CompanyProfileOptions>(configuration.GetSection(CompanyProfileOptions.SectionName));

        services.AddHttpClient<LmStudioProvider>();
        services.AddHttpClient<OpenAiProvider>();
        services.AddHttpClient<AnthropicProvider>();
        services.AddHttpClient<GroqProvider>();

        var maxConcurrentLlmCalls = configuration.GetValue<int?>("LlmProviders:MaxConcurrentCalls") ?? 4;
        services.AddSingleton(new LlmConcurrencyGate(maxConcurrentLlmCalls));

        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.LmStudio,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<LmStudioProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.OpenAi,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<OpenAiProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.Anthropic,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<AnthropicProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.Groq,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<GroqProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));

        services.AddScoped<IContentProviderFactory, ContentProviderFactory>();
        services.AddHttpClient<ISiteCrawlerService, SiteCrawlerService>();
        services.AddScoped<IKeywordHtmlParserService, KeywordHtmlParserService>();
        services.AddScoped<IContentPromptBuilder, ContentPromptBuilder>();
        services.AddScoped<ISoftwareApplicationSchemaBuilder, SoftwareApplicationSchemaBuilder>();
        services.AddScoped<ITechnicalArticleSchemaBuilder, TechnicalArticleSchemaBuilder>();
        services.AddScoped<IBlogPostingSchemaBuilder, BlogPostingSchemaBuilder>();
        services.AddScoped<IToolPageGenerator, ToolPageGenerator>();
        services.AddScoped<IContentGenerationOrchestrator, ContentGenerationOrchestrator>();
        services.AddHttpClient("GeekBackend");
        services.AddScoped<IGeekRepository, GeekRepository>();
        services.AddScoped<IMdxExportService, MdxExportService>();
        services.AddHttpClient("GitHub");
        services.AddScoped<IGeekatyourspotCommitService, GeekatyourspotCommitService>();
        services.AddSingleton<IJsonLdParserService, JsonLdParserService>();

        services.AddScoped<IEditorialReviewService, EditorialReviewService>();
        services.AddScoped<IReviewLoopService, ReviewLoopService>();

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
            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                logger.LogWarning("No Postgres connection configured — running on an in-memory store; data will not persist across restarts.");
            }

            logger.LogInformation("Content Writer database ready.");

            await SeedDefaultClientAsync(db, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content Writer database initialization failed.");
            if (!app.Environment.IsDevelopment())
                throw;
        }
    }

    /// <summary>
    /// Idempotent: seeds the "Geek At Your Spot" client wired to the existing GeekBackend on first
    /// run only, so v2 testing mirrors v1 behavior. DefaultAuthorId is left unset — publish fails
    /// with a clear error until an operator sets it via PUT /api/clients/{id}/publish-target.
    /// </summary>
    private static async Task SeedDefaultClientAsync(
        ContentWriterDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        const string DefaultClientName = "Geek At Your Spot";

        if (await db.Clients.AnyAsync(cancellationToken))
            return;

        var client = new Client { Name = DefaultClientName };
        client.PublishTarget = new PublishTarget
        {
            ClientId = client.Id,
            GeekBackendApiBaseUrl = "https://api.geekatyourspot.com",
            OAuthTokenEndpoint = "api/oauth/token",
            ClientIdEnvVar = "GEEKATYOURSPOT_OAUTH_CLIENT_ID",
            ClientSecretEnvVar = "GEEKATYOURSPOT_OAUTH_CLIENT_SECRET",
            CategoryStrategy = CategoryStrategy.DepartmentBased,
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded default client '{ClientName}' ({ClientId}).", DefaultClientName, client.Id);
    }
}

internal sealed record ContentWriterDatabaseOptions(string ConnectionString);
