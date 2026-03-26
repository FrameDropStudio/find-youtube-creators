using System.Net;
using System.Text.RegularExpressions;

namespace FindYouTubeCreator.App;

internal static partial class TextUtilities
{
    private static readonly HashSet<string> StopWords =
    [
        "about", "after", "all", "also", "and", "are", "been", "best", "build", "demo", "for", "from",
        "game", "games", "gaming", "have", "into", "just", "make", "more", "most", "that", "their",
        "them", "this", "with", "your", "will", "you", "new", "our", "over", "like", "single", "player"
    ];

    public static IReadOnlyList<string> ExtractKeywords(string text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = StripHtml(text).ToLowerInvariant();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WordRegex().Matches(normalized))
        {
            var word = match.Value.Trim();
            if (word.Length < 4 || StopWords.Contains(word))
            {
                continue;
            }

            counts[word] = counts.GetValueOrDefault(word) + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(pair => pair.Key)
            .ToList();
    }

    public static string StripHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutTags = HtmlRegex().Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    public static IReadOnlyList<string> DistinctPreservingOrder(IEnumerable<string?> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (seen.Add(normalized))
            {
                results.Add(normalized);
            }
        }

        return results;
    }

    public static string? ExtractFirstEmail(IEnumerable<string?> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var match = EmailRegex().Match(text);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ExtractUrls(IEnumerable<string?> texts, int maxCount = 5)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in UrlRegex().Matches(text))
            {
                var url = match.Value.TrimEnd('.', ',', ';', ')', ']');
                if (!seen.Add(url))
                {
                    continue;
                }

                urls.Add(url);
                if (urls.Count >= maxCount)
                {
                    return urls;
                }
            }
        }

        return urls;
    }

    public static bool ContainsAny(string text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0)
        {
            return false;
        }

        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "creator-search";
        }

        var cleaned = StripHtml(value).ToLowerInvariant();
        cleaned = NonSlugRegex().Replace(cleaned, "-");
        cleaned = DashRegex().Replace(cleaned, "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "creator-search" : cleaned;
    }

    [GeneratedRegex("[a-zA-Z0-9][a-zA-Z0-9\\-']{2,}")]
    private static partial Regex WordRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("https?://[^\\s<>\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex DashRegex();
}
