namespace FindYouTubeCreator.App;

internal static class LeadScorer
{
    public static IEnumerable<CreatorLead> ScoreAndFilter(
        IReadOnlyList<ChannelCandidate> candidates,
        GameTargetProfile profile,
        SearchQueryPlan queryPlan,
        DiscoveryConfig config)
    {
        var totalQueries = Math.Max(queryPlan.Requests.Count, 1);
        var targetTerms = TextUtilities.DistinctPreservingOrder(
            new[] { profile.Name }
                .Concat(profile.SearchTerms)
                .Concat(profile.SimilarGames)
                .Concat(profile.Genres));

        foreach (var candidate in candidates)
        {
            if (candidate.MatchedVideos.Count < config.Scoring.MinimumMatchedVideos)
            {
                continue;
            }

            if (candidate.SubscriberCount is < 1 || candidate.SubscriberCount < config.YouTube.MinSubscribers)
            {
                continue;
            }

            if (config.YouTube.MaxSubscribers is { } maxSubscribers &&
                candidate.SubscriberCount is { } subscriberCount &&
                subscriberCount > maxSubscribers)
            {
                continue;
            }

            var searchableText = string.Join(
                ' ',
                new[]
                {
                    candidate.ChannelTitle,
                    candidate.ChannelDescription ?? string.Empty,
                    string.Join(' ', candidate.MatchedVideos.Select(video => video.VideoTitle)),
                    string.Join(' ', candidate.MatchedVideos.Select(video => video.VideoDescription ?? string.Empty))
                });

            if (TextUtilities.ContainsAny(searchableText, profile.ExcludeTerms))
            {
                continue;
            }

            var publicEmail = TextUtilities.ExtractFirstEmail(
                new[] { candidate.ChannelDescription }
                    .Concat(candidate.MatchedVideos.Select(video => video.VideoDescription)));

            if (config.Scoring.RequirePublicEmail && string.IsNullOrWhiteSpace(publicEmail))
            {
                continue;
            }

            var matchedTermCount = targetTerms.Count(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
            var distinctSeedGames = candidate.MatchedSeedGames.Count;
            var primaryVideos = candidate.MatchedVideos.Count(video => video.IsPrimaryGame);
            var similarGameCoverage = candidate.MatchedSeedGames.Count(game => !string.Equals(game, profile.Name, StringComparison.OrdinalIgnoreCase));
            var queryCoverageRatio = (double)candidate.MatchedQueries.Count / totalQueries;
            var matchedVideosScore = Math.Min(candidate.MatchedVideos.Count * 8, 24);
            var queryCoverageScore = (int)Math.Round(queryCoverageRatio * 30);
            var termMatchScore = Math.Min(matchedTermCount * 4, 16);
            var distinctSeedGameScore = Math.Min(distinctSeedGames * 8, 24);
            var primaryCoverageScore = Math.Min(primaryVideos * 4, 12);
            var similarCoverageScore = Math.Min(similarGameCoverage * 4, 16);
            var subscriberScore = ScoreSubscribers(candidate.SubscriberCount, config.Scoring);
            var averageViewsScore = ScoreAverageViews(candidate.ViewCount, candidate.VideoCount);
            var emailBonus = string.IsNullOrWhiteSpace(publicEmail) ? 0 : 6;
            var fitScore = Math.Min(
                100,
                matchedVideosScore +
                queryCoverageScore +
                termMatchScore +
                distinctSeedGameScore +
                primaryCoverageScore +
                similarCoverageScore +
                subscriberScore +
                averageViewsScore +
                emailBonus);

            var notes = new List<string>
            {
                $"Matched {candidate.MatchedVideos.Count} videos across {candidate.MatchedQueries.Count} search queries."
            };

            if (distinctSeedGames > 0)
            {
                notes.Add($"Covered {distinctSeedGames} related titles from the Steam similarity search.");
            }

            if (matchedTermCount > 0)
            {
                notes.Add($"Matched {matchedTermCount} of your game/genre terms.");
            }

            if (candidate.SubscriberCount is { } subscribers)
            {
                notes.Add($"Subscribers: {subscribers:N0}.");
            }

            if (!string.IsNullOrWhiteSpace(publicEmail))
            {
                notes.Add("Public email detected in channel or video text.");
            }

            yield return new CreatorLead
            {
                ChannelId = candidate.ChannelId,
                ChannelTitle = string.IsNullOrWhiteSpace(candidate.ChannelTitle) ? candidate.ChannelId : candidate.ChannelTitle,
                ChannelUrl = $"https://www.youtube.com/channel/{candidate.ChannelId}",
                PublicEmail = publicEmail,
                ContactUrls = TextUtilities.ExtractUrls(
                    new[] { candidate.ChannelDescription }
                        .Concat(candidate.MatchedVideos.Select(video => video.VideoDescription))),
                Subscribers = candidate.SubscriberCount,
                VideosPublished = candidate.VideoCount,
                TotalViews = candidate.ViewCount,
                MatchedVideoCount = candidate.MatchedVideos.Count,
                MatchedQueryCount = candidate.MatchedQueries.Count,
                MatchedMarkets = candidate.MatchedMarkets
                    .OrderBy(market => market, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                MatchedSeedGames = candidate.MatchedSeedGames
                    .OrderBy(game => game, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SampleVideoTitles = candidate.MatchedVideos
                    .Select(video => video.VideoTitle)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList(),
                MatchedQueries = candidate.MatchedQueries
                    .OrderBy(query => query, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                FitNotes = notes,
                FitScore = fitScore
            };
        }
    }

    private static int ScoreSubscribers(long? subscriberCount, ScoringOptions options)
    {
        if (subscriberCount is null or <= 0)
        {
            return 0;
        }

        var subscribers = subscriberCount.Value;
        if (subscribers >= options.IdealMinSubscribers && subscribers <= options.IdealMaxSubscribers)
        {
            return 15;
        }

        if (subscribers < options.IdealMinSubscribers)
        {
            return (int)Math.Round(15d * subscribers / options.IdealMinSubscribers);
        }

        var ratio = (double)subscribers / options.IdealMaxSubscribers;
        return Math.Max(0, 15 - (int)Math.Round(Math.Log10(ratio) * 10));
    }

    private static int ScoreAverageViews(long? totalViews, long? videoCount)
    {
        if (totalViews is null or <= 0 || videoCount is null or <= 0)
        {
            return 0;
        }

        var averageViews = totalViews.Value / (double)videoCount.Value;
        if (averageViews >= 25_000)
        {
            return 10;
        }

        return averageViews switch
        {
            >= 10_000 => 8,
            >= 5_000 => 6,
            >= 1_500 => 4,
            >= 500 => 2,
            _ => 0
        };
    }
}
