using ContentWriter.Application.Providers;
using ContentWriter.Application.Services;

namespace ContentWriter.Application.Tests;

public class LlmResponseJsonParserImagePromptsTests
{
    private static string BuildPrompt(int words) => string.Join(" ", Enumerable.Repeat("visual", words));

    private static readonly IReadOnlyList<ImagePromptSectionTarget> ExpectedSections =
    [
        new ImagePromptSectionTarget("pillar", "Why reconciliation matters", 1),
        new ImagePromptSectionTarget("blog", "Getting started", 1),
    ];

    [Fact]
    public void ParseSectionImagePrompts_ValidJson_ReturnsDraft()
    {
        var pillarPrompt = BuildPrompt(80);
        var blogPrompt = BuildPrompt(60);
        var raw = $$"""
            {
              "sections": [
                {
                  "sourceType": "pillar",
                  "heading": "Why reconciliation matters",
                  "order": 1,
                  "prompt": "{{pillarPrompt}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false,
                  "notes": "Flat vector infographic; avoid readable text."
                },
                {
                  "sourceType": "blog",
                  "heading": "Getting started",
                  "order": 1,
                  "prompt": "{{blogPrompt}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false,
                  "notes": "Warm step-by-step feel."
                }
              ]
            }
            """;

        var draft = LlmResponseJsonParser.ParseSectionImagePrompts(raw, ExpectedSections, "image prompts");

        Assert.Equal(2, draft.Sections.Count);
        Assert.Equal(1536, draft.Sections[0].Width);
        Assert.Equal("Illustration", draft.Sections[0].StylePreset);
        Assert.True(draft.Sections[0].Alchemy);
        Assert.Equal("blog", draft.Sections[1].SourceType);
    }

    [Fact]
    public void ParseSectionImagePrompts_PromptTooShort_Throws()
    {
        var raw = $$"""
            {
              "sections": [
                {
                  "sourceType": "pillar",
                  "heading": "Why reconciliation matters",
                  "order": 1,
                  "prompt": "{{BuildPrompt(10)}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                },
                {
                  "sourceType": "blog",
                  "heading": "Getting started",
                  "order": 1,
                  "prompt": "{{BuildPrompt(60)}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                }
              ]
            }
            """;

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseSectionImagePrompts(raw, ExpectedSections, "image prompts"));
    }

    [Fact]
    public void ParseSectionImagePrompts_PortraitDimensions_Throws()
    {
        var raw = $$"""
            {
              "sections": [
                {
                  "sourceType": "pillar",
                  "heading": "Why reconciliation matters",
                  "order": 1,
                  "prompt": "{{BuildPrompt(80)}}",
                  "width": 1024,
                  "height": 1536,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                },
                {
                  "sourceType": "blog",
                  "heading": "Getting started",
                  "order": 1,
                  "prompt": "{{BuildPrompt(60)}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                }
              ]
            }
            """;

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseSectionImagePrompts(raw, ExpectedSections, "image prompts"));
    }

    [Fact]
    public void ParseSectionImagePrompts_MissingSection_Throws()
    {
        var raw = $$"""
            {
              "sections": [
                {
                  "sourceType": "pillar",
                  "heading": "Why reconciliation matters",
                  "order": 1,
                  "prompt": "{{BuildPrompt(80)}}",
                  "width": 1536,
                  "height": 1024,
                  "leonardoModel": "Leonardo Phoenix",
                  "stylePreset": "Illustration",
                  "alchemy": true,
                  "photoReal": false
                }
              ]
            }
            """;

        Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseSectionImagePrompts(raw, ExpectedSections, "image prompts"));
    }
}
