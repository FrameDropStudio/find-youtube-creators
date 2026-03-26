using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

namespace FindYouTubeCreator.App;

internal sealed partial class YouTubeDiscoveryClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ChannelCandidate>> DiscoverAsync(YouTubeOptions options, SearchQueryPlan plan)
    {
        var candidates = new Dictionary<string, ChannelCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in plan.Requests)
        {
            var hits = await SearchVideosAsync(options, request);
            foreach (var hit in hits)
            {
                if (!candidates.TryGetValue(hit.ChannelId, out var candidate))
                {
                    candidate = new ChannelCandidate
                    {
                        ChannelId = hit.ChannelId
                    };
                    candidates.Add(hit.ChannelId, candidate);
                }

                candidate.MatchedVideos.Add(hit);
                candidate.MatchedQueries.Add(hit.Query);
                candidate.MatchedMarkets.Add(hit.MarketName);
                candidate.MatchedSeedGames.Add(hit.SeedGameName);
            }
        }

        await EnrichChannelsAsync(options, candidates.Values.ToList());
        return candidates.Values.ToList();
    }

    private async Task<IReadOnlyList<SearchVideoHit>> SearchVideosAsync(YouTubeOptions options, SearchRequest request)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["part"] = "snippet",
            ["type"] = "video",
            ["order"] = "relevance",
            ["maxResults"] = options.ResultsPerQuery.ToString(CultureInfo.InvariantCulture),
            ["q"] = request.QueryText,
            ["key"] = options.ResolveApiKey(),
            ["relevanceLanguage"] = request.RelevanceLanguage
        };

        if (options.RestrictToGamingCategory)
        {
            parameters["videoCategoryId"] = "20";
        }

        if (!string.IsNullOrWhiteSpace(request.RegionCode))
        {
            parameters["regionCode"] = request.RegionCode;
        }

        if (options.PublishedWithinMonths > 0)
        {
            parameters["publishedAfter"] = DateTimeOffset.UtcNow
                .AddMonths(-options.PublishedWithinMonths)
                .ToString("O", CultureInfo.InvariantCulture);
        }

        var uri = $"https://www.googleapis.com/youtube/v3/search?{BuildQueryString(parameters)}";
        using var response = await httpClient.GetAsync(uri);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await BuildApiErrorMessage(response, "YouTube search"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        var results = new List<SearchVideoHit>();
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idNode) ||
                !idNode.TryGetProperty("videoId", out var videoIdNode) ||
                !item.TryGetProperty("snippet", out var snippet))
            {
                continue;
            }

            var channelId = GetString(snippet, "channelId");
            var videoId = videoIdNode.GetString();
            var title = GetString(snippet, "title");

            if (string.IsNullOrWhiteSpace(channelId) ||
                string.IsNullOrWhiteSpace(videoId) ||
                string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            results.Add(new SearchVideoHit
            {
                Query = request.QueryLabel,
                MarketName = request.MarketName,
                SeedGameName = request.SeedGameName,
                IsPrimaryGame = request.IsPrimaryGame,
                ChannelId = channelId,
                VideoId = videoId,
                VideoTitle = title,
                VideoDescription = GetString(snippet, "description"),
                PublishedAt = ParseDateTimeOffset(GetString(snippet, "publishedAt"))
            });
        }

        return results;
    }

    private async Task EnrichChannelsAsync(YouTubeOptions options, IReadOnlyList<ChannelCandidate> candidates)
    {
        foreach (var batch in candidates.Chunk(50))
        {
            var parameters = new Dictionary<string, string?>
            {
                ["part"] = "snippet,statistics",
                ["id"] = string.Join(",", batch.Select(candidate => candidate.ChannelId)),
                ["key"] = options.ResolveApiKey()
            };

            var uri = $"https://www.googleapis.com/youtube/v3/channels?{BuildQueryString(parameters)}";
            using var response = await httpClient.GetAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await BuildApiErrorMessage(response, "YouTube channels"));
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var byId = batch.ToDictionary(candidate => candidate.ChannelId, StringComparer.OrdinalIgnoreCase);

            foreach (var item in items.EnumerateArray())
            {
                var channelId = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(channelId) || !byId.TryGetValue(channelId, out var candidate))
                {
                    continue;
                }

                if (item.TryGetProperty("snippet", out var snippet))
                {
                    candidate.ChannelTitle = GetString(snippet, "title") ?? candidate.ChannelTitle;
                    candidate.ChannelDescription = GetString(snippet, "description");
                    candidate.CustomUrl = GetString(snippet, "customUrl");
                    candidate.Country = GetString(snippet, "country");
                    candidate.PublishedAt = ParseDateTimeOffset(GetString(snippet, "publishedAt"));
                }

                if (item.TryGetProperty("statistics", out var statistics))
                {
                    candidate.SubscriberCount = GetLong(statistics, "subscriberCount");
                    candidate.VideoCount = GetLong(statistics, "videoCount");
                    candidate.ViewCount = GetLong(statistics, "viewCount");
                }
            }
        }
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var builder = new StringBuilder();
        var isFirst = true;

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!isFirst)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
            isFirst = false;
        }

        return builder.ToString();
    }

    private static async Task<string> BuildApiErrorMessage(HttpResponseMessage response, string source)
    {
        var body = await response.Content.ReadAsStringAsync();
        var friendlyMessage = TryBuildFriendlyApiErrorMessage(response, body);
        return friendlyMessage ?? $"{source} request failed with {(int)response.StatusCode} {response.ReasonPhrase}.";
    }

    private static string? TryBuildFriendlyApiErrorMessage(HttpResponseMessage response, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            var rawMessage = GetString(error, "message");
            var message = CleanApiMessage(rawMessage);
            var reasons = ReadReasons(error);

            if (reasons.Contains("quotaExceeded", StringComparer.OrdinalIgnoreCase))
            {
                return "Your YouTube API daily quota has been exceeded. Wait for the quota to reset in Google Cloud, reduce search depth, or use another API key.";
            }

            if (reasons.Contains("keyInvalid", StringComparer.OrdinalIgnoreCase) ||
                reasons.Contains("badRequest", StringComparer.OrdinalIgnoreCase))
            {
                return "The YouTube API key is invalid or not accepted for this request. Check that the key is correct and that YouTube Data API v3 is enabled for the Google Cloud project.";
            }

            if (reasons.Contains("accessNotConfigured", StringComparer.OrdinalIgnoreCase) ||
                reasons.Contains("forbidden", StringComparer.OrdinalIgnoreCase))
            {
                return $"YouTube API access was refused. {message ?? "Check that YouTube Data API v3 is enabled and that the API key restrictions allow this app."}";
            }

            return message is not null
                ? $"YouTube API request failed: {message}"
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HashSet<string> ReadReasons(JsonElement error)
    {
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!error.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return reasons;
        }

        foreach (var item in errors.EnumerateArray())
        {
            var reason = GetString(item, "reason");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                reasons.Add(reason);
            }
        }

        return reasons;
    }

    private static string? CleanApiMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = HtmlTagRegex().Replace(decoded, string.Empty);
        return Regex.Replace(withoutTags, "\\s+", " ").Trim();
    }

    private static long? GetLong(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out numeric))
        {
            return numeric;
        }

        return null;
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();
}
