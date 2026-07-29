using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.Export;
using ContentWriter.Application.Services.JsonLd;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.Review;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure;
using ContentWriter.Infrastructure.InMemory;

namespace ContentWriter.Api.Hosting;

public static class ContentWriterServiceRegistration
{
    /// <summary>
    /// <paramref name="persistenceStoreFactory"/> lets a host that already owns a trusted
    /// GeekRepository connection (i.e. GeekAPI, post-merge) supply an <see cref="IPersistenceStore"/>
    /// backed by it, reusing that host's own credential — this project never constructs its own
    /// GeekRepository client or holds that credential (see AGENTS.md "Persistence and target
    /// architecture"). Defaults to the local filesystem when omitted (standalone/dev use).
    /// </summary>
    public static IServiceCollection AddContentWriter(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IPersistenceStore>? persistenceStoreFactory = null)
    {
        var dataDirectory = configuration["ContentWriter:DataDirectory"] ?? "./data";
        services.AddSingleton<IPersistenceStore>(sp =>
            persistenceStoreFactory?.Invoke(sp)
            ?? new FileSystemPersistenceStore(dataDirectory, sp.GetRequiredService<ILogger<FileSystemPersistenceStore>>()));

        // Project/Client stores with durable backing
        services.AddSingleton<IProjectStore>(sp =>
            new PersistentProjectStore(
                sp.GetRequiredService<IPersistenceStore>(),
                sp.GetRequiredService<IClientStore>(),
                sp.GetRequiredService<ILogger<PersistentProjectStore>>()));

        services.AddSingleton<IClientStore>(sp =>
            new PersistentClientStore(
                sp.GetRequiredService<IPersistenceStore>(),
                sp.GetRequiredService<ILogger<PersistentClientStore>>()));

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
        services.AddScoped<IHtmlExportService, HtmlExportService>();
        services.AddHttpClient("GitHub");
        services.AddScoped<IGeekatyourspotCommitService, GeekatyourspotCommitService>();
        services.AddSingleton<IJsonLdParserService, JsonLdParserService>();

        services.AddScoped<IEditorialReviewService, EditorialReviewService>();
        services.AddScoped<IReviewLoopService, ReviewLoopService>();

        return services;
    }

    /// <summary>Hydrates all persisted clients and projects from storage at startup.</summary>
    public static async Task HydrateContentWriterAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ContentWriter.Startup");

        await using var scope = app.Services.CreateAsyncScope();
        var clientStore = scope.ServiceProvider.GetRequiredService<IClientStore>();
        var projectStore = scope.ServiceProvider.GetRequiredService<IProjectStore>();

        const string DefaultClientName = "Geek At Your Spot";

        // Hydrate persisted clients first
        if (clientStore is PersistentClientStore persistentClientStore)
        {
            await persistentClientStore.HydrateAsync(cancellationToken);
        }

        // Then hydrate persisted projects (which rehydrate their Client refs)
        if (projectStore is PersistentProjectStore persistentProjectStore)
        {
            await persistentProjectStore.HydrateAsync(cancellationToken);
        }

        // If no clients exist (fresh start), seed the default
        if (!await clientStore.AnyAsync(cancellationToken))
        {
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

            await clientStore.AddAsync(client, cancellationToken);
            logger.LogInformation("Seeded default client '{ClientName}' ({ClientId}).", DefaultClientName, client.Id);
        }

        logger.LogInformation("ContentWriter initialization complete.");
    }
}
