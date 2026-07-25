using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using ContentWriter.Application.Services.PromptBuilders;
using ContentWriter.Application.Services.SchemaBuilders;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContentWriter.Application.Tests;

public class ToolPageSoftSaveTests
{
    [Fact]
    public async Task GenerateToolPagesAsync_short_body_still_returns_and_saves_word_count()
    {
        var toolsHeading = "Top AI Tools for Testing";
        var pillarBody = new ContentDocument(
            MakeSection("h2", "Lede", "lede text"),
            [
                new Section(
                    "h2",
                    toolsHeading,
                    [new TextParagraph([new Run("Intro to tools.")])],
                    null,
                    [MakeSection("h3", "ShortApp", "A thin pillar blurb about ShortApp.")]),
            ]);

        var articleRow = new GeneratedContent
        {
            ProjectId = Guid.NewGuid(),
            ContentType = GeneratedContentType.TechnicalArticle,
            Title = "Pillar",
            Body = pillarBody,
            SectionOutline = [toolsHeading],
        };

        var project = new Project { Id = articleRow.ProjectId, Department = "accounting" };
        var metadata = new ArticleMetadataDraft("Pillar", "Meta", ["kw"], [toolsHeading]);
        var context = MakeContext();
        var provider = new ScriptedProvider([ShortToolBodyJson(), ToolMetadataJson()]);

        var generator = new ToolPageGenerator(
            new SoftwareApplicationSchemaBuilder(),
            new ContentPromptBuilder(),
            NullLogger<ToolPageGenerator>.Instance);

        var result = await generator.GenerateToolPagesAsync(
            project,
            articleRow,
            metadata,
            context,
            provider,
            "https://example.com/articles/accounting/pillar",
            cancellationToken: CancellationToken.None);

        Assert.Equal(ToolGenerationOutcome.Success, result.Outcome);
        Assert.Single(result.ToolPosts);
        var tool = result.ToolPosts[0];
        Assert.True(tool.WordCount > 0);
        Assert.True(tool.WordCount < ContentLengthTargets.ToolMinWords);
        Assert.Equal("ShortApp", tool.Title);
        Assert.NotNull(tool.Body);
    }

    [Fact]
    public void Assembler_includes_tool_word_count()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Department = "accounting",
            GeneratedContents =
            [
                new GeneratedContent
                {
                    ContentType = GeneratedContentType.ToolPost,
                    Title = "ShortApp",
                    Slug = "shortapp",
                    MetaDescription = "desc",
                    WordCount = 420,
                    SourceAppOrder = 1,
                    Body = new ContentDocument(MakeSection("h2", "Overview", "words"), []),
                },
            ],
        };

        var set = GeneratedContentSetAssembler.Assemble(
            project, "accounting",
            "https://example.com/articles",
            "https://example.com/blog",
            "https://example.com/tools");

        Assert.NotNull(set.ToolPosts);
        Assert.Equal(420, set.ToolPosts![0].WordCount);
    }

    private static Section MakeSection(string tag, string heading, string text) =>
        new(tag, heading, [new TextParagraph([new Run(text)])], null, []);

    private static ProjectGenerationContext MakeContext() => new(
        ProjectName: "Project",
        ProjectUrl: "https://example.com",
        TargetKeyword: "invoice automation",
        Department: "accounting",
        SiteName: "Example",
        DetectedTone: "professional",
        DetectedFocus: "AI implementation",
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Acme Consulting",
        PublisherLogoUrl: "https://example.com/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://example.com/articles",
        BlogBaseUrl: "https://example.com/blog",
        ToolBaseUrl: "https://example.com/tools",
        ImplementerPositioning: "hands-on AI implementer",
        Provider: LlmProviderType.OpenAi);

    private static string ShortToolBodyJson()
    {
        static string Section(string heading, string text) =>
            $$"""{"tag":"h2","heading":"{{heading}}","paragraphs":[{"type":"text","runs":[{"text":"{{text}}","bold":false,"italic":false,"href":null}]}],"imagePrompt":null,"children":[]}""";

        return $$"""{"sections":[{{Section("Overview", "Short overview text about ShortApp.")}},{{Section("Key Capabilities", "A couple capabilities only.")}},{{Section("Implementation Considerations", "Thin implementation notes.")}},{{Section("When to Use", "Use when requirements are simple.")}}]}""";
    }

    private static string ToolMetadataJson() =>
        """{"departmentListExcerpt":"a","summary":"b","mainSummary":"c","heroSummary":"d","homeSummary":"e","blogSummary":"f","toolPageExcerpt":"g","advertisingSummary":"h longer promo","metaDescription":"meta for shortapp tool page seo"}""";

    private sealed class ScriptedProvider : IContentGenerationProvider
    {
        private readonly Queue<string> _responses;

        public ScriptedProvider(IEnumerable<string> responses) =>
            _responses = new Queue<string>(responses);

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No scripted LLM responses left.");
            }

            return Task.FromResult(new ChatCompletionResult(_responses.Dequeue(), "test-model", 1, 1));
        }
    }
}
