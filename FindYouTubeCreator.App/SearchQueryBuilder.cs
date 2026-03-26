namespace FindYouTubeCreator.App;

internal static class SearchQueryBuilder
{
    public static SearchQueryPlan Build(GameTargetProfile profile, DiscoveryConfig config)
    {
        var seeds = TextUtilities.DistinctPreservingOrder(
            new[] { profile.Name }
                .Concat(profile.SimilarGames)
                .Concat(profile.SearchTerms)
                .Concat(profile.Genres)
                .Concat(profile.Categories)
                .Concat(profile.Tags));

        var requests = new List<SearchRequest>();
        foreach (var market in config.YouTube.ResolveMarkets())
        {
            foreach (var request in BuildMarketRequests(profile, config.YouTube, market))
            {
                requests.Add(request);
            }
        }

        return new SearchQueryPlan
        {
            Requests = requests,
            SeedTerms = seeds
        };
    }

    private static IReadOnlyList<SearchRequest> BuildMarketRequests(
        GameTargetProfile profile,
        YouTubeOptions options,
        YouTubeMarketOptions market)
    {
        var requests = new List<SearchRequest>();

        foreach (var term in market.QueryTerms)
        {
            AddRequest(requests, market, profile.Name, true, $"\"{profile.Name}\" {term}");
        }

        foreach (var similarGame in profile.SimilarGames)
        {
            foreach (var term in market.QueryTerms.Take(2))
            {
                if (requests.Count >= options.MaxQueries)
                {
                    break;
                }

                AddRequest(requests, market, similarGame, false, $"\"{similarGame}\" {term}");
            }

            if (requests.Count >= options.MaxQueries)
            {
                break;
            }
        }

        foreach (var nicheTerm in profile.SearchTerms.Take(4))
        {
            if (requests.Count >= options.MaxQueries)
            {
                break;
            }

            foreach (var term in market.QueryTerms.Take(2))
            {
                AddRequest(requests, market, profile.Name, true, $"{nicheTerm} {term}");
            }
        }

        return requests.Take(options.MaxQueries).ToList();
    }

    private static void AddRequest(
        ICollection<SearchRequest> requests,
        YouTubeMarketOptions market,
        string seedGameName,
        bool isPrimaryGame,
        string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return;
        }

        var queryLabel = $"{market.Name}: {queryText}";
        if (requests.Any(request => string.Equals(request.QueryLabel, queryLabel, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        requests.Add(new SearchRequest
        {
            QueryText = queryText,
            QueryLabel = queryLabel,
            MarketName = market.Name,
            SeedGameName = seedGameName,
            IsPrimaryGame = isPrimaryGame,
            RelevanceLanguage = market.RelevanceLanguage,
            RegionCode = market.RegionCode
        });
    }
}
