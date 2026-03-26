namespace FindYouTubeCreator.App;

internal sealed class DiscoveryRunner
{
    public async Task<DiscoveryRunResult> RunAsync(DiscoveryConfig config, CancellationToken cancellationToken = default)
    {
        config.Validate();

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FindYouTubeCreator/0.1");

        var steamClient = new SteamMetadataClient(httpClient);
        var steamProfile = config.Steam.AppId is { } appId
            ? await steamClient.GetGameProfileAsync(appId, config.Steam)
            : new SteamGameProfile();

        cancellationToken.ThrowIfCancellationRequested();

        var targetProfile = GameTargetProfile.Merge(steamProfile, config.Game);
        var queryPlan = SearchQueryBuilder.Build(targetProfile, config);

        var discoveryClient = new YouTubeDiscoveryClient(httpClient);
        var candidates = await discoveryClient.DiscoverAsync(config.YouTube, queryPlan);
        var orderedLeads = LeadExporter.OrderLeads(
            LeadScorer.ScoreAndFilter(candidates, targetProfile, queryPlan, config).ToList());

        var exporter = new LeadExporter();
        var report = await exporter.ExportAsync(config.Export, targetProfile, queryPlan, orderedLeads);

        return new DiscoveryRunResult
        {
            Game = targetProfile,
            QueryPlan = queryPlan,
            Leads = orderedLeads,
            Report = report
        };
    }
}

internal sealed class DiscoveryRunResult
{
    public required GameTargetProfile Game { get; init; }
    public required SearchQueryPlan QueryPlan { get; init; }
    public required IReadOnlyList<CreatorLead> Leads { get; init; }
    public required ExportReport Report { get; init; }
}
