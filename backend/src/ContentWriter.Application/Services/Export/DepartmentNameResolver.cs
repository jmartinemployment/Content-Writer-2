using System.Text;
using ContentWriter.Application.Providers;

namespace ContentWriter.Application.Services.Export;

public static partial class DepartmentNameResolver
{
    public static string Resolve(string? departmentOverride)
    {
        if (!string.IsNullOrWhiteSpace(departmentOverride))
            return departmentOverride.Trim();

        throw new ContentGenerationException(
            "A department must be provided explicitly to /publish — no fallback resolution is performed.");
    }

    public static string SanitizeDirectorySegment(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch == '-')
                sb.Append(ch);
            else if (ch is ' ' or '_')
                sb.Append('-');
        }

        var result = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "general" : result;
    }

    /// <summary>
    /// Human-readable folder name for exports: {department}/{topic}/Pillar|Blog|...
    /// Preserves keyword casing and spaces; strips characters invalid on the local filesystem.
    /// </summary>
    public static string ResolveTopicFolder(string? targetKeyword, string slugFallback)
    {
        var fromKeyword = SanitizeTopicFolderName(targetKeyword);
        if (!string.IsNullOrEmpty(fromKeyword))
            return fromKeyword;

        var fromSlug = SanitizeTopicFolderName(slugFallback.Replace('-', ' '));
        if (!string.IsNullOrEmpty(fromSlug))
            return fromSlug;

        return "general";
    }

    public static string SanitizeTopicFolderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars()
            .Concat(['/', '\\', ':'])
            .Distinct()
            .ToArray();
        var sb = new StringBuilder();
        var lastWasSpace = false;

        foreach (var ch in value.Trim())
        {
            if (invalid.Contains(ch))
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }
}
