using System.Text.RegularExpressions;

namespace ContentWriter.Application.Services;

public static class SlugHelper
{
    public static string Slugify(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var withoutDiacritics = Regex.Replace(lower, @"[^a-z0-9\s-]", "");
        var collapsed = Regex.Replace(withoutDiacritics, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(collapsed) ? Guid.NewGuid().ToString("N")[..8] : collapsed;
    }

    public static string EnsureUniqueSlug(string baseSlug, ISet<string> usedSlugs)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? Guid.NewGuid().ToString("N")[..8] : baseSlug;
        if (usedSlugs.Add(slug))
        {
            return slug;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (usedSlugs.Add(candidate))
            {
                return candidate;
            }
        }

        var fallback = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        usedSlugs.Add(fallback);
        return fallback;
    }
}
