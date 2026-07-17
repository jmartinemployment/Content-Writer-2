namespace ContentWriter.Domain.Enums;

public enum LlmProviderType
{
    LmStudio = 0,
    OpenAi = 1,
    Anthropic = 2
}

public enum ProjectStatus
{
    Draft = 0,
    Crawling = 1,
    ReadyForGeneration = 2,
    Generating = 3,
    Completed = 4,
    Failed = 5
}

public enum KeywordSourceCategory
{
    KeywordResult = 0,
    EduDomain = 1,
    GovDomain = 2,
    Wikipedia = 3,
    Local = 4,
    PeopleAlsoAsk = 5,
    CompetitorCrawl = 6
}

public enum GeneratedContentType
{
    TechnicalArticle = 0,
    BlogPost = 1,
    SocialFacebook = 2,
    SocialLinkedIn = 3,
    EmailColdOutreach = 4,
    EmailNewsletter = 5,      // stub
    EmailStoryNurture = 6,     // stub
    EmailTransactional = 7,    // stub
    ImagePromptPillarFigure = 8,
    ImagePromptSocialFacebook = 9,
    ImagePromptSocialLinkedIn = 10,
    ImagePromptSection = 11,
    ToolPost = 12
}

/// <summary>How a client's published URLs are grouped into categories/departments.</summary>
public enum CategoryStrategy
{
    /// <summary>use-cases/{dept}, blog/{dept}, tools/{dept} — the existing Geek At Your Spot convention.</summary>
    DepartmentBased = 0,

    /// <summary>Client supplies category slugs directly, no department convention assumed.</summary>
    FreeForm = 1
}

public enum BatchJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum BatchJobItemStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Pipeline steps a batch item passes through, in order. Tools, Blog, and Social run concurrently
/// via Task.WhenAll (see BatchWorker) despite having distinct enum values here — each still gets
/// its own tracked row since they can fail/retry independently.
/// </summary>
public enum BatchStepName
{
    Crawl = 0,
    Plan = 1,
    Body = 2,
    Tools = 3,
    Blog = 4,
    Social = 5,
    Email = 6,
    ImagePrompts = 7,
    Review = 8,
    Publish = 9
}

public enum BatchStepStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}
