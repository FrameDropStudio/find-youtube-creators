using System.Text;
using System.Text.Json;

namespace FindYouTubeCreator.App;

internal sealed class LeadExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<ExportReport> ExportAsync(
        ExportOptions options,
        GameTargetProfile game,
        SearchQueryPlan queryPlan,
        IReadOnlyList<CreatorLead> leads)
    {
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var orderedLeads = OrderLeads(leads);

        var suffix = options.IncludeTimestampInFileNames
            ? $"-{DateTime.UtcNow:yyyyMMdd-HHmmss}"
            : string.Empty;

        var csvPath = Path.Combine(outputDirectory, $"{options.FileNamePrefix}{suffix}.csv");
        var jsonPath = Path.Combine(outputDirectory, $"{options.FileNamePrefix}{suffix}.json");

        await File.WriteAllTextAsync(csvPath, BuildCsv(orderedLeads), Encoding.UTF8);

        var payload = new
        {
            generatedAtUtc = DateTime.UtcNow,
            game,
            queryPlan,
            leads = orderedLeads
        };

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);

        return new ExportReport
        {
            CsvPath = csvPath,
            JsonPath = jsonPath
        };
    }

    public static IReadOnlyList<CreatorLead> OrderLeads(IReadOnlyList<CreatorLead> leads)
    {
        return leads
            .OrderByDescending(lead => lead.FitScore)
            .ThenByDescending(lead => lead.MatchedSeedGames.Count)
            .ThenByDescending(lead => lead.MatchedVideoCount)
            .ThenByDescending(lead => lead.Subscribers ?? 0)
            .ToList();
    }

    private static string BuildCsv(IReadOnlyList<CreatorLead> leads)
    {
        var rows = new List<string>
        {
            string.Join(',',
                "Rank",
                "FitScore",
                "ChannelTitle",
                "ChannelUrl",
                "Subscribers",
                "VideosPublished",
                "TotalViews",
                "MatchedVideoCount",
                "MatchedQueryCount",
                "MatchedMarkets",
                "MatchedSeedGames",
                "PublicEmail",
                "ContactUrls",
                "MatchedQueries",
                "SampleVideoTitles",
                "FitNotes")
        };

        for (var index = 0; index < leads.Count; index++)
        {
            var lead = leads[index];
            rows.Add(string.Join(',',
                Escape(index + 1),
                Escape(lead.FitScore),
                Escape(lead.ChannelTitle),
                Escape(lead.ChannelUrl),
                Escape(lead.Subscribers),
                Escape(lead.VideosPublished),
                Escape(lead.TotalViews),
                Escape(lead.MatchedVideoCount),
                Escape(lead.MatchedQueryCount),
                Escape(string.Join(" | ", lead.MatchedMarkets)),
                Escape(string.Join(" | ", lead.MatchedSeedGames)),
                Escape(lead.PublicEmail),
                Escape(string.Join(" | ", lead.ContactUrls)),
                Escape(string.Join(" | ", lead.MatchedQueries)),
                Escape(string.Join(" | ", lead.SampleVideoTitles)),
                Escape(string.Join(" | ", lead.FitNotes))));
        }

        return string.Join(Environment.NewLine, rows);
    }

    private static string Escape(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
