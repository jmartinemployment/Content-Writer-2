using ContentWriter.Application.Services.Review;

namespace ContentWriter.Application.Tests;

public class ReviewRevisionNotesTests
{
    [Fact]
    public void ExtractRevisionNotes_from_reviewer_json_returns_notes_field()
    {
        var raw = """{"verdict":"revise","notes":"1. [Section: \"Overview\"] Add a concrete deployment example."}""";

        var notes = ReviewLoopService.ExtractRevisionNotes(raw);

        Assert.Equal("1. [Section: \"Overview\"] Add a concrete deployment example.", notes);
    }

    [Fact]
    public void ExtractRevisionNotes_from_plain_text_returns_trimmed_text()
    {
        var raw = "  Tighten the Key Capabilities list.  ";

        var notes = ReviewLoopService.ExtractRevisionNotes(raw);

        Assert.Equal("Tighten the Key Capabilities list.", notes);
    }

    [Fact]
    public void ExtractRevisionNotes_empty_or_missing_notes_returns_empty()
    {
        Assert.Equal(string.Empty, ReviewLoopService.ExtractRevisionNotes(""));
        Assert.Equal(string.Empty, ReviewLoopService.ExtractRevisionNotes("""{"verdict":"approved"}"""));
        Assert.Equal(string.Empty, ReviewLoopService.ExtractRevisionNotes("""{"verdict":"revise","notes":"   "}"""));
    }
}
