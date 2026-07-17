namespace ContentWriter.Application.DTOs;

/// <summary>Shared metadata used to stamp out both the TechnicalArticle and BlogPosting JSON+LD schemas.</summary>
public record ContentMetadata(
    string Headline,
    string Description,
    string AuthorName,
    string PublisherName,
    string PublisherLogoUrl,
    string CanonicalUrl,
    string MainImageUrl,
    DateTime DatePublishedUtc,
    DateTime DateModifiedUtc,
    List<string> Keywords,
    int WordCount);
