namespace ContentWriter.Application.Services.JsonLd;

public interface IJsonLdParserService
{
    JsonLdSiteSummary Summarize(IReadOnlyList<string> rawBlocks);
}
