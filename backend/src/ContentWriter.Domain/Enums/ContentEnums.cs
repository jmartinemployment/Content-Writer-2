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
