namespace FindYouTubeCreator.App;

public sealed class DiscoveryConfig
{
    public SteamOptions Steam { get; init; } = new();
    public GameProfileOptions Game { get; init; } = new();
    public YouTubeOptions YouTube { get; init; } = new();
    public ScoringOptions Scoring { get; init; } = new();
    public ExportOptions Export { get; init; } = new();

    public void Validate()
    {
        if (Steam.AppId is null && string.IsNullOrWhiteSpace(Game.Name))
        {
            throw new InvalidOperationException("Set steam.appId or game.name in the config.");
        }

        if (string.IsNullOrWhiteSpace(YouTube.ResolveApiKey()))
        {
            throw new InvalidOperationException("Set youtube.apiKey or provide youtube.apiKeyEnvironmentVariable.");
        }

        if (YouTube.ResultsPerQuery is < 1 or > 50)
        {
            throw new InvalidOperationException("youtube.resultsPerQuery must be between 1 and 50.");
        }

        if (YouTube.MaxQueries < 1)
        {
            throw new InvalidOperationException("youtube.maxQueries must be at least 1.");
        }

        foreach (var market in YouTube.ResolveMarkets())
        {
            if (string.IsNullOrWhiteSpace(market.Name))
            {
                throw new InvalidOperationException("Each youtube market needs a name.");
            }

            if (market.QueryTerms.Count == 0)
            {
                throw new InvalidOperationException($"youtube market '{market.Name}' must define at least one query term.");
            }
        }

        if (Scoring.MinimumMatchedVideos < 1)
        {
            throw new InvalidOperationException("scoring.minimumMatchedVideos must be at least 1.");
        }
    }
}

public sealed class SteamOptions
{
    public int? AppId { get; init; }
    public bool DiscoverSimilarGames { get; init; } = true;
    public int SimilarGameLimit { get; init; } = 10;
    public int TagLimit { get; init; } = 12;
}

public sealed class GameProfileOptions
{
    public string? Name { get; init; }
    public string? Website { get; init; }
    public string? Description { get; init; }
    public List<string> SearchTerms { get; init; } = [];
    public List<string> SimilarGames { get; init; } = [];
    public List<string> ExcludeTerms { get; init; } = [];
}

public sealed class YouTubeOptions
{
    public string? ApiKey { get; init; }
    public string ApiKeyEnvironmentVariable { get; init; } = "YOUTUBE_API_KEY";
    public int MaxQueries { get; init; } = 10;
    public int ResultsPerQuery { get; init; } = 25;
    public bool RestrictToGamingCategory { get; init; } = true;
    public string RelevanceLanguage { get; init; } = "en";
    public string? RegionCode { get; init; }
    public List<YouTubeMarketOptions> Markets { get; init; } = [];
    public int PublishedWithinMonths { get; init; } = 36;
    public long MinSubscribers { get; init; }
    public long? MaxSubscribers { get; init; }

    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        return Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) ?? string.Empty;
    }

    public IReadOnlyList<YouTubeMarketOptions> ResolveMarkets()
    {
        if (Markets.Count > 0)
        {
            return Markets;
        }

        return
        [
            new YouTubeMarketOptions
            {
                Name = "English",
                RelevanceLanguage = RelevanceLanguage,
                RegionCode = RegionCode,
                QueryTerms = ["gameplay", "review", "let's play", "demo", "preview"]
            }
        ];
    }
}

public sealed class YouTubeMarketOptions
{
    public string Name { get; init; } = string.Empty;
    public string RelevanceLanguage { get; init; } = "en";
    public string? RegionCode { get; init; }
    public List<string> QueryTerms { get; init; } = [];
}

public sealed class ScoringOptions
{
    public int IdealMinSubscribers { get; init; } = 1_000;
    public int IdealMaxSubscribers { get; init; } = 500_000;
    public int MinimumMatchedVideos { get; init; } = 1;
    public bool RequirePublicEmail { get; init; }
}

public sealed class ExportOptions
{
    public string OutputDirectory { get; init; } = "output";
    public string FileNamePrefix { get; init; } = "youtube-leads";
    public bool IncludeTimestampInFileNames { get; init; } = true;
}
