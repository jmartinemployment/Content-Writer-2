using ContentWriter.Application.DTOs;
using ContentWriter.Domain.Enums;

namespace ContentWriter.Api.Contracts;

public record CreateProjectRequest(Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department, LlmProviderType PreferredProvider);

public record ProjectSummaryResponse(
    Guid Id, Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department,
    ProjectStatus Status, LlmProviderType PreferredProvider, DateTime CreatedAtUtc);

public record ProjectDetailResponse(
    Guid Id, Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department, ProjectStatus Status,
    LlmProviderType PreferredProvider, CrawlSummaryResponse? Crawl,
    List<KeywordSourceResponse> KeywordSources, List<GeneratedContentResponse> GeneratedContent,
    GeneratedContentSet? ContentSet);

public record CrawlSummaryResponse(
    string SiteName, int PagesCrawled, string DetectedTone, string DetectedFocus,
    int HeadingCount, int ParagraphCount, int JsonLdBlockCount);

public record KeywordSourceResponse(
    Guid Id, KeywordSourceCategory Category, string OriginalFileName,
    string? ExtractedTitle, int HeadingCount, int ParagraphCount, int QuestionCount);

public record GeneratedContentResponse(
    Guid Id, GeneratedContentType ContentType, string Title, string Slug,
    string? MetaDescription, IReadOnlyList<string> Keywords, int WordCount,
    string BodyHtml, string? JsonLdSchema, string? RelatedArticleUrl, DateTime CreatedAtUtc);
