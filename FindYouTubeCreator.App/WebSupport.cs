using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FindYouTubeCreator.App;

internal sealed class DownloadRegistry
{
    private readonly ConcurrentDictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

    public string Register(string filePath)
    {
        var token = Guid.NewGuid().ToString("N");
        files[token] = Path.GetFullPath(filePath);
        return token;
    }

    public bool TryResolve(string token, out string filePath)
    {
        return files.TryGetValue(token, out filePath!);
    }
}

internal sealed class DiscoveryWebRequest
{
    public string SteamInput { get; init; } = string.Empty;
    public string? GameName { get; init; }
    public string? ApiKey { get; init; }
    public string SearchTerms { get; init; } = string.Empty;
    public string SimilarGames { get; init; } = string.Empty;
    public string ExcludeTerms { get; init; } = string.Empty;
    public List<string> Markets { get; init; } = [];
    public string Depth { get; init; } = "balanced";
    public int PublishedWithinMonths { get; init; } = 24;
    public long MinSubscribers { get; init; } = 1_000;
    public long? MaxSubscribers { get; init; } = 750_000;
    public bool RequirePublicEmail { get; init; }
}

internal sealed class DiscoveryDefaultsResponse
{
    public required DiscoveryWebRequest Defaults { get; init; }
    public required IReadOnlyList<MarketDescriptor> Markets { get; init; }
    public required IReadOnlyList<DepthDescriptor> Depths { get; init; }
}

internal sealed class MarketDescriptor
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Language { get; init; }
    public required string Region { get; init; }
}

internal sealed class DepthDescriptor
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }
}

internal sealed class DiscoveryWebResponse
{
    public required string GameName { get; init; }
    public required int SearchRequestsExecuted { get; init; }
    public required int CreatorLeadCount { get; init; }
    public required string CsvPath { get; init; }
    public required string JsonPath { get; init; }
    public required string CsvDownloadUrl { get; init; }
    public required string JsonDownloadUrl { get; init; }
    public required IReadOnlyList<string> SeedGames { get; init; }
    public required IReadOnlyList<CreatorLead> Leads { get; init; }
}

internal static class MarketCatalog
{
    private static readonly IReadOnlyDictionary<string, YouTubeMarketOptions> Presets =
        new Dictionary<string, YouTubeMarketOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-us"] = new()
            {
                Name = "English",
                RelevanceLanguage = "en",
                RegionCode = "US",
                QueryTerms = ["gameplay", "review", "let's play", "demo", "preview"]
            },
            ["fr-fr"] = new()
            {
                Name = "French",
                RelevanceLanguage = "fr",
                RegionCode = "FR",
                QueryTerms = ["gameplay", "test", "avis", "découverte", "aperçu"]
            },
            ["zh-cn"] = new()
            {
                Name = "Chinese",
                RelevanceLanguage = "zh-CN",
                RegionCode = "TW",
                QueryTerms = ["实况", "评测", "试玩", "游戏演示"]
            },
            ["ja-jp"] = new()
            {
                Name = "Japanese",
                RelevanceLanguage = "ja",
                RegionCode = "JP",
                QueryTerms = ["実況", "レビュー", "プレイ動画", "紹介"]
            },
            ["pt-br"] = new()
            {
                Name = "Brazilian Portuguese",
                RelevanceLanguage = "pt-BR",
                RegionCode = "BR",
                QueryTerms = ["gameplay", "analise", "análise", "detonado", "review"]
            },
            ["de-de"] = new()
            {
                Name = "German",
                RelevanceLanguage = "de",
                RegionCode = "DE",
                QueryTerms = ["gameplay", "lets play", "let's play", "review", "test"]
            }
        };

    public static IReadOnlyList<string> DefaultSelection =>
        ["en-us", "fr-fr", "zh-cn", "ja-jp", "pt-br", "de-de"];

    public static IReadOnlyList<YouTubeMarketOptions> ResolveSelected(IEnumerable<string> selectedKeys)
    {
        var keys = selectedKeys?.ToList() ?? [];
        if (keys.Count == 0)
        {
            keys = DefaultSelection.ToList();
        }

        return keys
            .Where(Presets.ContainsKey)
            .Select(key => Presets[key])
            .ToList();
    }

    public static IReadOnlyList<MarketDescriptor> DescribeAll()
    {
        return Presets.Select(pair => new MarketDescriptor
        {
            Key = pair.Key,
            Name = pair.Value.Name,
            Language = pair.Value.RelevanceLanguage,
            Region = pair.Value.RegionCode ?? "global"
        }).ToList();
    }
}

internal static class DepthCatalog
{
    private static readonly IReadOnlyDictionary<string, DepthPreset> Presets =
        new Dictionary<string, DepthPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["focused"] = new("Focused", "Fast shortlist, lower quota use.", 5, 10, 8, 2),
            ["balanced"] = new("Balanced", "Best default for broad outreach.", 8, 15, 12, 1),
            ["deep"] = new("Deep", "Wider net across similar games and markets.", 12, 20, 16, 1)
        };

    public static DepthPreset Resolve(string? key)
    {
        return key is not null && Presets.TryGetValue(key, out var preset)
            ? preset
            : Presets["balanced"];
    }

    public static IReadOnlyList<DepthDescriptor> DescribeAll()
    {
        return Presets.Select(pair => new DepthDescriptor
        {
            Key = pair.Key,
            Name = pair.Value.Name,
            Summary = pair.Value.Summary
        }).ToList();
    }
}

internal sealed record DepthPreset(
    string Name,
    string Summary,
    int MaxQueries,
    int ResultsPerQuery,
    int SimilarGameLimit,
    int MinimumMatchedVideos);

internal static partial class WebConfigFactory
{
    public static DiscoveryConfig Create(DiscoveryWebRequest request)
    {
        var depth = DepthCatalog.Resolve(request.Depth);
        var appId = ParseSteamAppId(request.SteamInput);
        var gameName = string.IsNullOrWhiteSpace(request.GameName) ? null : request.GameName.Trim();
        var markets = MarketCatalog.ResolveSelected(request.Markets);
        var baseName = gameName ?? request.SteamInput;

        return new DiscoveryConfig
        {
            Steam = new SteamOptions
            {
                AppId = appId,
                DiscoverSimilarGames = true,
                SimilarGameLimit = depth.SimilarGameLimit,
                TagLimit = 12
            },
            Game = new GameProfileOptions
            {
                Name = gameName,
                SearchTerms = ParseLines(request.SearchTerms),
                SimilarGames = ParseLines(request.SimilarGames),
                ExcludeTerms = ParseLines(request.ExcludeTerms)
            },
            YouTube = new YouTubeOptions
            {
                ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim(),
                MaxQueries = depth.MaxQueries,
                ResultsPerQuery = depth.ResultsPerQuery,
                RestrictToGamingCategory = true,
                Markets = markets.ToList(),
                PublishedWithinMonths = Math.Max(1, request.PublishedWithinMonths),
                MinSubscribers = Math.Max(0, request.MinSubscribers),
                MaxSubscribers = request.MaxSubscribers
            },
            Scoring = new ScoringOptions
            {
                IdealMinSubscribers = 5_000,
                IdealMaxSubscribers = 300_000,
                MinimumMatchedVideos = depth.MinimumMatchedVideos,
                RequirePublicEmail = request.RequirePublicEmail
            },
            Export = new ExportOptions
            {
                OutputDirectory = Path.Combine("output", "web"),
                FileNamePrefix = $"{TextUtilities.Slugify(baseName)}-youtube-creators",
                IncludeTimestampInFileNames = true
            }
        };
    }

    public static DiscoveryDefaultsResponse CreateDefaults()
    {
        return new DiscoveryDefaultsResponse
        {
            Defaults = new DiscoveryWebRequest
            {
                Markets = MarketCatalog.DefaultSelection.ToList(),
                Depth = "balanced",
                PublishedWithinMonths = 24,
                MinSubscribers = 1_000,
                MaxSubscribers = 750_000
            },
            Markets = MarketCatalog.DescribeAll(),
            Depths = DepthCatalog.DescribeAll()
        };
    }

    private static int? ParseSteamAppId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SteamAppIdRegex().Match(value);
        return match.Success && int.TryParse(match.Groups["appid"].Value, out var appId)
            ? appId
            : null;
    }

    private static List<string> ParseLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [GeneratedRegex("(?<appid>\\d{3,})")]
    private static partial Regex SteamAppIdRegex();
}
