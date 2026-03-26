# FindYouTubeCreator

This repository contains a small discovery tool for building a ranked outreach list of YouTube creators who are likely to cover your Steam game.

## What it does

- Pulls your game metadata from Steam by `appId` or lets you override it manually.
- Pulls Steam tags and `More like this` recommendations to discover similar games automatically.
- Generates a search plan from your game name, Steam-similar titles, genres, categories, tags, and custom search terms.
- Searches YouTube for gaming videos across one or more language/region markets.
- Enriches channels with public YouTube stats.
- Scores and filters channels into a CSV/JSON outreach list.

## Requirements

- .NET 8 SDK
- A YouTube Data API v3 key

## Quick start

1. Set your API key in an environment variable:

```powershell
$env:YOUTUBE_API_KEY="your-key-here"
```

2. Edit `docs/sample-config.json`:
   - Set `steam.appId` to your Steam app.
   - Or leave `steam.appId` empty and set `game.name` manually.
   - Leave `steam.discoverSimilarGames` enabled if you want the tool to expand from Steam's similarity graph.
   - Add strong `game.searchTerms` that describe the niche.
   - Optionally add `game.similarGames` if you want to force extra adjacent titles beyond Steam's recommendations.
   - Tune the `youtube.markets` list for the languages and regions you want to target, for example English, French, Chinese, Japanese, Brazilian Portuguese, and German.
   - Tighten `youtube.minSubscribers`, `youtube.maxSubscribers`, and `scoring.minimumMatchedVideos`.

3. Run the tool:

```powershell
dotnet run --project .\FindYouTubeCreator.App\FindYouTubeCreator.App.csproj -- --config .\docs\sample-config.json
```

## Local web app

Double-click [Launch-WebApp.bat](K:/GameDev/FindYoutubeCreator/Launch-WebApp.bat) to start the local web UI and open it in your browser.

Manual equivalent:

```powershell
dotnet run --project .\FindYouTubeCreator.App\FindYouTubeCreator.App.csproj
```

## Output

The tool writes:

- `output/*.csv`: outreach spreadsheet
- `output/*.json`: full machine-readable report

Useful CSV columns:

- `FitScore`: overall rank
- `MatchedVideoCount`: how many relevant videos were found
- `MatchedQueries`: which searches found the channel
- `MatchedMarkets`: which language/region markets surfaced the channel
- `MatchedSeedGames`: which Steam-similar games or target-game searches surfaced the channel
- `PublicEmail`: only public emails detected in channel or video text
- `ContactUrls`: public URLs found in descriptions

## Notes

- This uses the YouTube Data API, so search calls consume quota.
- `youtube.maxQueries` applies per market, so adding more markets increases quota usage proportionally.
- The API does not expose every creator contact detail. The tool only captures public emails and links it can see in returned text.
- The initial ranking is a lead-generation pass. You should still review the top results before outreach.
