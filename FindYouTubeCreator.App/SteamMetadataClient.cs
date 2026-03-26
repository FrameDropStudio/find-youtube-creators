using System.Text.Json;
using System.Text.RegularExpressions;

namespace FindYouTubeCreator.App;

internal sealed partial class SteamMetadataClient(HttpClient httpClient)
{
    public async Task<SteamGameProfile> GetGameProfileAsync(int appId, SteamOptions options)
    {
        var uri = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english";
        using var response = await httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty(appId.ToString(), out var appNode))
        {
            throw new InvalidOperationException($"Steam response did not contain app {appId}.");
        }

        if (!appNode.TryGetProperty("success", out var successNode) || !successNode.GetBoolean())
        {
            throw new InvalidOperationException($"Steam app {appId} was not found.");
        }

        var data = appNode.GetProperty("data");
        var storePageHtml = await GetStorePageHtmlAsync(appId);
        var moreLikeThisHtml = options.DiscoverSimilarGames
            ? await GetMoreLikeThisHtmlAsync(appId)
            : string.Empty;
        var tags = ReadTagsFromStoreHtml(storePageHtml, options.TagLimit);
        var similarGames = options.DiscoverSimilarGames
            ? ReadSimilarGames(storePageHtml, moreLikeThisHtml, appId, options.SimilarGameLimit)
            : [];

        return new SteamGameProfile
        {
            Name = GetString(data, "name"),
            ShortDescription = GetString(data, "short_description"),
            Website = GetString(data, "website"),
            Genres = ReadDescriptionArray(data, "genres"),
            Categories = ReadDescriptionArray(data, "categories"),
            Tags = tags,
            Developers = ReadStringArray(data, "developers"),
            Publishers = ReadStringArray(data, "publishers"),
            SimilarGames = similarGames
        };
    }

    private async Task<string> GetStorePageHtmlAsync(int appId)
    {
        var uri = $"https://store.steampowered.com/app/{appId}/?l=english";
        using var response = await httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetMoreLikeThisHtmlAsync(int appId)
    {
        var uri = $"https://store.steampowered.com/recommended/morelike/app/{appId}/?l=english";
        using var response = await httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string? GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> ReadDescriptionArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Select(item => item.TryGetProperty("description", out var description) ? description.GetString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static IReadOnlyList<string> ReadTagsFromStoreHtml(string html, int limit)
    {
        var match = TagsRegex().Match(html);
        if (!match.Success)
        {
            return [];
        }

        using var document = JsonDocument.Parse(match.Groups["tagsJson"].Value);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return document.RootElement.EnumerateArray()
            .Select(tag => tag.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<RelatedGame> ReadSimilarGames(string storePageHtml, string moreLikeThisHtml, int appId, int limit)
    {
        if (string.IsNullOrWhiteSpace(moreLikeThisHtml))
        {
            return [];
        }

        var targetTagIds = ReadPrimaryTagIds(moreLikeThisHtml);
        var relatedGames = ReadRelatedGamesFromMoreLikeThisHtml(moreLikeThisHtml, appId, targetTagIds);

        if (relatedGames.Count == 0)
        {
            return [];
        }

        var namesById = ReadRelatedGameNamesFromStoreHtml(storePageHtml);

        return relatedGames
            .Where(game => namesById.ContainsKey(game.AppId ?? 0))
            .Select(game => new RelatedGame
            {
                AppId = game.AppId,
                Name = namesById[game.AppId!.Value],
                SharedTagCount = game.SharedTagCount
            })
            .OrderByDescending(game => game.SharedTagCount)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static List<RelatedGame> ReadRelatedGamesFromMoreLikeThisHtml(string html, int appId, HashSet<int> targetTagIds)
    {
        var releasedSectionMatch = ReleasedSectionRegex().Match(html);
        var sectionHtml = releasedSectionMatch.Success ? releasedSectionMatch.Groups["section"].Value : html;
        var results = new List<RelatedGame>();
        var seen = new HashSet<int>();

        foreach (Match match in SimilarGridGameRegex().Matches(sectionHtml))
        {
            if (!int.TryParse(match.Groups["id"].Value, out var relatedAppId) || relatedAppId == appId || !seen.Add(relatedAppId))
            {
                continue;
            }

            var sharedTagCount = match.Groups["tagIds"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var parsed) ? parsed : (int?)null)
                .Count(value => value.HasValue && targetTagIds.Contains(value.Value));

            results.Add(new RelatedGame
            {
                AppId = relatedAppId,
                Name = relatedAppId.ToString(),
                SharedTagCount = sharedTagCount
            });
        }

        return results;
    }

    private static Dictionary<int, string> ReadRelatedGameNamesFromStoreHtml(string html)
    {
        var namesById = new Dictionary<int, string>();

        foreach (Match match in RelatedGameRegex().Matches(html))
        {
            if (!int.TryParse(match.Groups["id"].Value, out var relatedAppId) || namesById.ContainsKey(relatedAppId))
            {
                continue;
            }

            var encodedName = match.Groups["name"].Value;
            var decodedName = JsonSerializer.Deserialize<string>($"\"{encodedName}\"");
            if (!string.IsNullOrWhiteSpace(decodedName))
            {
                namesById[relatedAppId] = decodedName;
            }
        }

        return namesById;
    }

    private static HashSet<int> ReadPrimaryTagIds(string html)
    {
        var match = PrimaryTagIdsRegex().Match(html);
        if (!match.Success)
        {
            return [];
        }

        return match.Groups["tagIds"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet();
    }

    [GeneratedRegex("InitAppTagModal\\(\\s*\\d+,\\s*(?<tagsJson>\\[.*?\\])\\s*,\\s*\\[", RegexOptions.Singleline)]
    private static partial Regex TagsRegex();

    [GeneratedRegex("\"(?<id>\\d+)\":\\{\"name\":\"(?<name>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline)]
    private static partial Regex RelatedGameRegex();

    [GeneratedRegex("<div class=\"recommendation_area_ctn similar_grid_ctn\" id=\"released\".*?(?<section><div class=\"recommendation_area_ctn similar_grid_ctn\" id=\"released\".*?</div>\\s*?</div>)\\s*<h2 class=\"morelike_section_divider\">", RegexOptions.Singleline)]
    private static partial Regex ReleasedSectionRegex();

    [GeneratedRegex("class=\"similar_grid_capsule\"\\s+data-ds-appid=\"(?<id>\\d+)\".*?data-ds-tagids=\"\\[(?<tagIds>[0-9,]+)\\]\"", RegexOptions.Singleline)]
    private static partial Regex SimilarGridGameRegex();

    [GeneratedRegex("class=\"header_image\".*?data-ds-tagids=\"\\[(?<tagIds>[0-9,]+)\\]\"", RegexOptions.Singleline)]
    private static partial Regex PrimaryTagIdsRegex();
}
