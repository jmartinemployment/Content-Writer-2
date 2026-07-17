using System.Text.Json;
using System.Text.RegularExpressions;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Providers;

namespace ContentWriter.Application.Services;

/// <summary>Parses JSON-shaped LLM responses with light repair for common local-model mistakes.</summary>
public static class LlmResponseJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex MarkdownFence = new(@"^```(?:json|html)?\s*|\s*```$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLink = new(@"\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static T Parse<T>(string rawContent, string label)
    {
        var cleaned = Clean(rawContent);

        foreach (var candidate in CandidateJsonStrings(cleaned))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<T>(candidate, JsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Try the next repaired candidate.
            }
        }

        var isTruncated = cleaned.Contains('{') && !cleaned.TrimEnd().EndsWith('}');
        var hint = isTruncated
            ? " The response looks truncated — try a smaller local model context window or use OpenAI/Anthropic for long-form content."
            : string.Empty;

        throw new ContentGenerationException(
            $"Model did not return valid JSON for {label}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}.{hint}");
    }

    public static string ParseHtmlBody(string rawContent, string label)
    {
        var cleaned = Clean(rawContent);

        if (cleaned.StartsWith('{'))
        {
            foreach (var candidate in CandidateJsonStrings(cleaned))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<BodyHtmlResponse>(candidate, JsonOptions);
                    if (!string.IsNullOrWhiteSpace(parsed?.BodyHtml))
                    {
                        return HtmlBodyNormalizer.Normalize(parsed.BodyHtml);
                    }
                }
                catch (JsonException)
                {
                    // Fall through to salvage.
                }
            }

            var bodyMatch = Regex.Match(cleaned, @"""bodyHtml""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline);
            if (bodyMatch.Success)
            {
                return HtmlBodyNormalizer.Normalize(UnescapeJsonString(bodyMatch.Groups[1].Value));
            }
        }

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ContentGenerationException($"Model returned empty HTML for {label}.");
        }

        return HtmlBodyNormalizer.Normalize(cleaned);
    }

    public static string ParseSocialText(string rawContent, string articleUrl, string label)
    {
        var cleaned = Clean(rawContent);

        foreach (var candidate in CandidateJsonStrings(cleaned))
        {
            if (TryDeserializeSocial(candidate, out var text))
            {
                return NormalizeSocialText(text, articleUrl);
            }
        }

        var strictMatch = Regex.Match(cleaned, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline);
        if (strictMatch.Success)
        {
            return NormalizeSocialText(UnescapeJsonString(strictMatch.Groups[1].Value), articleUrl);
        }

        // Truncated or broken JSON — salvage the text field and ensure the link is present.
        var salvageMatch = Regex.Match(cleaned, @"""text""\s*:\s*""(.*)", RegexOptions.Singleline);
        if (salvageMatch.Success)
        {
            var salvaged = UnescapeJsonString(salvageMatch.Groups[1].Value.TrimEnd('"', ' ', '\r', '\n', '}'));
            if (salvaged.Length > 0)
            {
                return NormalizeSocialText(salvaged, articleUrl);
            }
        }

        throw new ContentGenerationException(
            $"Model did not return valid JSON for {label}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}");
    }

    public static ColdOutreachEmailDraft ParseColdOutreach(string rawContent, string label)
    {
        var cleaned = Clean(rawContent);

        foreach (var candidate in CandidateJsonStrings(cleaned))
        {
            if (TryDeserializeColdOutreach(candidate, out var draft))
            {
                return ValidateColdOutreach(draft, label);
            }
        }

        throw new ContentGenerationException(
            $"Model did not return valid JSON for {label}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}");
    }

    public static ImagePromptSectionPromptsDraft ParseSectionImagePrompts(
        string rawContent,
        IReadOnlyList<ImagePromptSectionTarget> expectedSections,
        string label)
    {
        var cleaned = Clean(rawContent);

        foreach (var candidate in CandidateJsonStrings(cleaned))
        {
            if (TryDeserializeSectionImagePrompts(candidate, out var draft))
            {
                return ValidateSectionImagePrompts(draft, expectedSections, label);
            }
        }

        throw new ContentGenerationException(
            $"Model did not return valid JSON for {label}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}");
    }

    private static bool TryDeserializeSectionImagePrompts(string json, out ImagePromptSectionPromptsDraft draft)
    {
        draft = new ImagePromptSectionPromptsDraft([]);

        try
        {
            var parsed = JsonSerializer.Deserialize<ImagePromptSectionsResponse>(json, JsonOptions);
            if (parsed?.Sections is null || parsed.Sections.Count == 0)
            {
                return false;
            }

            draft = new ImagePromptSectionPromptsDraft(
                parsed.Sections.Select(ToSectionDraft).ToList());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ImagePromptSectionDraft ToSectionDraft(ImagePromptSectionResponse item) =>
        new(
            (item.SourceType ?? "").Trim().ToLowerInvariant(),
            (item.Heading ?? "").Trim(),
            item.Order,
            (item.Prompt ?? "").Trim(),
            item.Width,
            item.Height,
            (item.LeonardoModel ?? "").Trim(),
            (item.StylePreset ?? "").Trim(),
            item.Alchemy ?? true,
            item.PhotoReal ?? false,
            string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim());

    private static ImagePromptSectionPromptsDraft ValidateSectionImagePrompts(
        ImagePromptSectionPromptsDraft draft,
        IReadOnlyList<ImagePromptSectionTarget> expectedSections,
        string label)
    {
        if (draft.Sections.Count != expectedSections.Count)
        {
            throw new ContentGenerationException(
                $"{label} must include {expectedSections.Count} section prompts (got {draft.Sections.Count}).");
        }

        for (var i = 0; i < expectedSections.Count; i++)
        {
            var expected = expectedSections[i];
            var item = draft.Sections.FirstOrDefault(s =>
                string.Equals(s.SourceType, expected.SourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Heading, expected.Heading, StringComparison.OrdinalIgnoreCase)
                && s.Order == expected.Order);

            if (item is null)
            {
                throw new ContentGenerationException(
                    $"{label} is missing a prompt for {expected.SourceType} section \"{expected.Heading}\" (order {expected.Order}).");
            }

            ValidateSectionImagePromptItem(item, expected, label);
        }

        return draft;
    }

    private static void ValidateSectionImagePromptItem(
        ImagePromptSectionDraft item,
        ImagePromptSectionTarget expected,
        string label)
    {
        if (string.IsNullOrWhiteSpace(item.Prompt))
        {
            throw new ContentGenerationException(
                $"Model returned empty prompt for {expected.SourceType} section \"{expected.Heading}\" in {label}.");
        }

        var words = item.Prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < ImagePromptDefaults.PromptMinWords || words > ImagePromptDefaults.PromptMaxWords)
        {
            throw new ContentGenerationException(
                $"Prompt for \"{expected.Heading}\" must be {ImagePromptDefaults.PromptMinWords}–{ImagePromptDefaults.PromptMaxWords} words (got {words}).");
        }

        if (item.Width < 512 || item.Height < 512 || item.Width > 2048 || item.Height > 2048)
        {
            throw new ContentGenerationException($"Dimensions for \"{expected.Heading}\" are out of range (512–2048).");
        }

        if (item.Width < item.Height)
        {
            throw new ContentGenerationException($"Prompt for \"{expected.Heading}\" should be landscape (width >= height).");
        }

        if (string.IsNullOrWhiteSpace(item.LeonardoModel))
        {
            throw new ContentGenerationException($"Model returned empty leonardoModel for \"{expected.Heading}\" in {label}.");
        }

        if (string.IsNullOrWhiteSpace(item.StylePreset))
        {
            throw new ContentGenerationException($"Model returned empty stylePreset for \"{expected.Heading}\" in {label}.");
        }
    }

    private static IEnumerable<string> CandidateJsonStrings(string cleaned)
    {
        yield return cleaned;

        var extracted = ExtractJsonObject(cleaned);
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            yield return extracted;
            yield return RepairLiteralNewlinesInJsonStrings(extracted);
        }
    }

    private static bool TryDeserializeColdOutreach(string json, out ColdOutreachEmailDraft draft)
    {
        draft = new ColdOutreachEmailDraft("", "", "");
        try
        {
            var parsed = JsonSerializer.Deserialize<ColdOutreachResponse>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            draft = new ColdOutreachEmailDraft(
                (parsed.Subject ?? "").Trim(),
                (parsed.BodyText ?? "").Trim(),
                (parsed.CtaLabel ?? "").Trim());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ColdOutreachEmailDraft ValidateColdOutreach(ColdOutreachEmailDraft draft, string label)
    {
        if (string.IsNullOrWhiteSpace(draft.Subject))
        {
            throw new ContentGenerationException($"Model returned empty subject for {label}.");
        }

        if (string.IsNullOrWhiteSpace(draft.BodyText))
        {
            throw new ContentGenerationException($"Model returned empty body for {label}.");
        }

        if (string.IsNullOrWhiteSpace(draft.CtaLabel))
        {
            throw new ContentGenerationException($"Model returned empty ctaLabel for {label}.");
        }

        var words = draft.BodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < ContentLengthTargets.EmailColdOutreachMinWords || words > ContentLengthTargets.EmailColdOutreachMaxWords)
        {
            throw new ContentGenerationException(
                $"Cold outreach body must be {ContentLengthTargets.EmailColdOutreachMinWords}–{ContentLengthTargets.EmailColdOutreachMaxWords} words (got {words}).");
        }

        return draft;
    }

    private static bool TryDeserializeSocial(string json, out string text)
    {
        text = string.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<SocialTextResponse>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(parsed?.Text))
            {
                return false;
            }

            text = parsed.Text;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Clean(string rawContent) => MarkdownFence.Replace(rawContent, string.Empty).Trim();

    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return raw[start..(end + 1)];
    }

    private static string RepairLiteralNewlinesInJsonStrings(string json)
    {
        var result = new System.Text.StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        foreach (var ch in json)
        {
            if (escaped)
            {
                result.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                result.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                result.Append(ch);
                continue;
            }

            if (inString && (ch == '\r' || ch == '\n'))
            {
                result.Append("\\n");
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch (JsonException)
        {
            return value.Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
    }

    private static string NormalizeSocialText(string text, string articleUrl)
    {
        text = MarkdownLink.Replace(text, "$2").Trim();
        if (!text.Contains(articleUrl, StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text.TrimEnd()} {articleUrl}".Trim();
        }

        return text;
    }

    private sealed record SocialTextResponse(string Text);

    private sealed record BodyHtmlResponse(string BodyHtml);

    private sealed record ColdOutreachResponse(string? Subject, string? BodyText, string? CtaLabel);

    private sealed record ImagePromptSectionsResponse(IReadOnlyList<ImagePromptSectionResponse>? Sections);

    private sealed record ImagePromptSectionResponse(
        string? SourceType,
        string? Heading,
        int Order,
        string? Prompt,
        int Width,
        int Height,
        string? LeonardoModel,
        string? StylePreset,
        bool? Alchemy,
        bool? PhotoReal,
        string? Notes);
}
