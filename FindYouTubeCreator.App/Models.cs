namespace FindYouTubeCreator.App;

public sealed class SteamGameProfile
{
    public string? Name { get; init; }
    public string? ShortDescription { get; init; }
    public string? Website { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Developers { get; init; } = [];
    public IReadOnlyList<string> Publishers { get; init; } = [];
    public IReadOnlyList<RelatedGame> SimilarGames { get; init; } = [];
}

public sealed class RelatedGame
{
    public int? AppId { get; init; }
    public required string Name { get; init; }
    public int SharedTagCount { get; init; }
}

public sealed class GameTargetProfile
{
    public required string Name { get; init; }
    public string? Website { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Developers { get; init; } = [];
    public IReadOnlyList<string> Publishers { get; init; } = [];
    public IReadOnlyList<string> SearchTerms { get; init; } = [];
    public IReadOnlyList<string> SimilarGames { get; init; } = [];
    public IReadOnlyList<string> ExcludeTerms { get; init; } = [];

    public static GameTargetProfile Merge(SteamGameProfile steamProfile, GameProfileOptions options)
    {
        var description = FirstNonEmpty(options.Description, steamProfile.ShortDescription);
        var searchTerms = TextUtilities.DistinctPreservingOrder(
            options.SearchTerms
                .Concat(steamProfile.Genres)
                .Concat(steamProfile.Categories)
                .Concat(TextUtilities.ExtractKeywords(description ?? string.Empty, 12)));

        return new GameTargetProfile
        {
            Name = FirstNonEmpty(options.Name, steamProfile.Name)
                ?? throw new InvalidOperationException("Game name could not be determined."),
            Website = FirstNonEmpty(options.Website, steamProfile.Website),
            Description = description,
            Genres = TextUtilities.DistinctPreservingOrder(steamProfile.Genres),
            Categories = TextUtilities.DistinctPreservingOrder(steamProfile.Categories),
            Tags = TextUtilities.DistinctPreservingOrder(steamProfile.Tags),
            Developers = TextUtilities.DistinctPreservingOrder(steamProfile.Developers),
            Publishers = TextUtilities.DistinctPreservingOrder(steamProfile.Publishers),
            SearchTerms = searchTerms,
            SimilarGames = TextUtilities.DistinctPreservingOrder(
                steamProfile.SimilarGames.Select(game => game.Name)
                    .Concat(options.SimilarGames)),
            ExcludeTerms = TextUtilities.DistinctPreservingOrder(options.ExcludeTerms)
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public sealed class SearchQueryPlan
{
    public required IReadOnlyList<SearchRequest> Requests { get; init; }
    public required IReadOnlyList<string> SeedTerms { get; init; }
}

public sealed class SearchRequest
{
    public required string QueryText { get; init; }
    public required string QueryLabel { get; init; }
    public required string MarketName { get; init; }
    public required string SeedGameName { get; init; }
    public bool IsPrimaryGame { get; init; }
    public required string RelevanceLanguage { get; init; }
    public string? RegionCode { get; init; }
}

public sealed class SearchVideoHit
{
    public required string Query { get; init; }
    public required string MarketName { get; init; }
    public required string SeedGameName { get; init; }
    public bool IsPrimaryGame { get; init; }
    public required string VideoId { get; init; }
    public required string ChannelId { get; init; }
    public required string VideoTitle { get; init; }
    public string? VideoDescription { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed class ChannelCandidate
{
    public required string ChannelId { get; init; }
    public string ChannelTitle { get; set; } = string.Empty;
    public string? ChannelDescription { get; set; }
    public string? CustomUrl { get; set; }
    public string? Country { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long? SubscriberCount { get; set; }
    public long? VideoCount { get; set; }
    public long? ViewCount { get; set; }
    public List<SearchVideoHit> MatchedVideos { get; } = [];
    public HashSet<string> MatchedQueries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MatchedMarkets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MatchedSeedGames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CreatorLead
{
    public required string ChannelId { get; init; }
    public required string ChannelTitle { get; init; }
    public required string ChannelUrl { get; init; }
    public string? PublicEmail { get; init; }
    public IReadOnlyList<string> ContactUrls { get; init; } = [];
    public long? Subscribers { get; init; }
    public long? VideosPublished { get; init; }
    public long? TotalViews { get; init; }
    public int MatchedVideoCount { get; init; }
    public int MatchedQueryCount { get; init; }
    public IReadOnlyList<string> MatchedMarkets { get; init; } = [];
    public IReadOnlyList<string> MatchedSeedGames { get; init; } = [];
    public IReadOnlyList<string> SampleVideoTitles { get; init; } = [];
    public IReadOnlyList<string> MatchedQueries { get; init; } = [];
    public IReadOnlyList<string> FitNotes { get; init; } = [];
    public int FitScore { get; init; }
}

public sealed class ExportReport
{
    public required string CsvPath { get; init; }
    public required string JsonPath { get; init; }
}
