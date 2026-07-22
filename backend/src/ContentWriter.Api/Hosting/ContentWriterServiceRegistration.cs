using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.Export;
using ContentWriter.Application.Services.JsonLd;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.Review;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure.InMemory;

namespace ContentWriter.Api.Hosting;

public static class ContentWriterServiceRegistration
{
    public static IServiceCollection AddContentWriter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // No database. content-writer-v2 holds Project/Client state in memory for the process
        // lifetime and durably saves output only by committing .html to the geekatyourspot GitHub
        // repo (GeekatyourspotCommitService). State is gone on restart — that's expected.
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<IClientStore, ClientStore>();

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

    /// <summary>Idempotent: seeds the "Geek At Your Spot" client into the in-memory store on first run only.</summary>
    public static async Task SeedContentWriterDefaultsAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ContentWriter.Startup");

        await using var scope = app.Services.CreateAsyncScope();
        var clientStore = scope.ServiceProvider.GetRequiredService<IClientStore>();

        const string DefaultClientName = "Geek At Your Spot";

        if (await clientStore.AnyAsync(cancellationToken))
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

        await clientStore.AddAsync(client, cancellationToken);
        logger.LogInformation("Seeded default client '{ClientName}' ({ClientId}).", DefaultClientName, client.Id);
    }
}
