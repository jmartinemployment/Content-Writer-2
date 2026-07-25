using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;
using Xunit;

namespace ContentWriter.Application.Tests;

public class LlmResponseJsonParserSectionTests
{
    private const string ValidSectionJson =
        """{"tag":"h2","heading":"The Role of AI in Streamlining Tax Processes","paragraphs":[{"type":"text","runs":[{"text":"Tax compliance involves many interlocking steps."}]}]}""";

    [Fact]
    public void ParseSection_ValidJson_ReturnsSection()
    {
        var section = LlmResponseJsonParser.ParseSection(ValidSectionJson, "h2", "test section");

        Assert.Equal("The Role of AI in Streamlining Tax Processes", section.Heading);
        Assert.Single(section.Paragraphs);
    }

    [Fact]
    public void ParseSection_TrailingProseContainingStrayBrace_StillParses()
    {
        // A model that appends a closing remark after otherwise-valid JSON, where that remark
        // happens to contain a "}" character, previously broke the naive first-"{"-to-last-"}"
        // extraction — it would splice the trailing prose into the "JSON" handed to the
        // deserializer. The real bug was in extraction, not in what the model returned.
        var raw = ValidSectionJson + "\n\nLet me know if you'd like any changes to the {structure}.";

        var section = LlmResponseJsonParser.ParseSection(raw, "h2", "test section");

        Assert.Equal("The Role of AI in Streamlining Tax Processes", section.Heading);
    }

    [Fact]
    public void ParseSection_LeadingPreambleAndTrailingBrace_StillParses()
    {
        var raw = "Here is the requested section:\n" + ValidSectionJson + "\n\nHope this helps! {done}";

        var section = LlmResponseJsonParser.ParseSection(raw, "h2", "test section");

        Assert.Equal("The Role of AI in Streamlining Tax Processes", section.Heading);
    }

    [Fact]
    public void ParseSection_GenuinelyTruncatedJson_ThrowsWithTruncationHint()
    {
        var truncated = """{"tag":"h2","heading":"Incomplete Section","paragraphs":[{"type":"text","runs":[{"text":"This response got cut""";

        var ex = Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseSection(truncated, "h2", "test section"));

        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
