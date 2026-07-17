using System.Text.RegularExpressions;

namespace ContentWriter.Application.Services;

/// <summary>
/// Lightweight, dependency-free heuristics for estimating writing tone and topical focus
/// from crawled text. This runs before any LLM call so the prompt builder can steer the
/// model ("write in a conversational, second-person tone about managed IT services...")
/// instead of asking the model to guess tone from raw HTML.
/// </summary>
public static class ToneFocusAnalyzer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","if","then","of","to","in","on","for","with","as","by","at",
        "is","are","was","were","be","been","being","this","that","these","those","it","its","from",
        "we","you","your","our","us","they","their","he","she","his","her","them","i","not","can",
        "will","would","should","could","may","might","have","has","had","do","does","did","so","also",
        "about","into","over","under","more","most","such","than","how","what","when","where","why",
        "who","which","all","any","each","other","some","no","nor","just","only","up","out","off"
    };

    private static readonly Regex WordPattern = new(@"[A-Za-z][A-Za-z\-']{2,}", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[\.!\?])\s+", RegexOptions.Compiled);
    private static readonly Regex ContractionPattern = new(@"\b\w+'(?:re|ve|ll|d|s|t|m)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string DetectFocus(IEnumerable<string> headings, IEnumerable<string> paragraphs, int topN = 6)
    {
        var text = string.Join(' ', headings.Concat(paragraphs));
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WordPattern.Matches(text))
        {
            var word = match.Value.ToLowerInvariant();
            if (StopWords.Contains(word) || word.Length < 4)
            {
                continue;
            }
            frequency[word] = frequency.GetValueOrDefault(word) + 1;
        }

        var topTerms = frequency
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(topN)
            .Select(kv => kv.Key);

        return string.Join(", ", topTerms);
    }

    public static string DetectTone(IEnumerable<string> paragraphs)
    {
        var allText = string.Join(' ', paragraphs);
        if (string.IsNullOrWhiteSpace(allText))
        {
            return "Professional, neutral";
        }

        var sentences = SentenceSplit.Split(allText).Where(s => s.Trim().Length > 0).ToList();
        var words = WordPattern.Matches(allText).Select(m => m.Value).ToList();

        if (words.Count == 0 || sentences.Count == 0)
        {
            return "Professional, neutral";
        }

        var avgSentenceLength = (double)words.Count / sentences.Count;
        var longWordRatio = words.Count(w => w.Length >= 9) / (double)words.Count;
        var contractionCount = ContractionPattern.Matches(allText).Count;
        var secondPersonCount = Regex.Matches(allText, @"\byou\b|\byour\b", RegexOptions.IgnoreCase).Count;

        var casualSignal = contractionCount + secondPersonCount / 2.0;
        var technicalSignal = longWordRatio * 100 + Math.Max(0, avgSentenceLength - 18);

        return (casualSignal, technicalSignal) switch
        {
            _ when technicalSignal > 25 && casualSignal < 5 => "Technical, authoritative",
            _ when casualSignal > 15 => "Conversational, approachable",
            _ when technicalSignal > 15 => "Formal, professional",
            _ => "Balanced, professional with approachable framing"
        };
    }
}
