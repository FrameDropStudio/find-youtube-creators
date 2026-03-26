using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FindYouTubeCreator.App;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (TryGetConfigPath(args, out var configPath))
        {
            return await RunCliAsync(configPath);
        }

        var projectRoot = ResolveProjectRoot();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = projectRoot,
            WebRootPath = Path.Combine(projectRoot, "wwwroot")
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Services.AddSingleton<DiscoveryRunner>();
        builder.Services.AddSingleton<DownloadRegistry>();

        var app = builder.Build();
        var url = GetWebUrl(args);
        app.Urls.Add(url);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/defaults", () => Results.Json(WebConfigFactory.CreateDefaults(), AppJson.Options));

        app.MapPost("/api/discover", async Task<IResult> (
            DiscoveryWebRequest request,
            DiscoveryRunner runner,
            DownloadRegistry downloads,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var config = WebConfigFactory.Create(request);
                var result = await runner.RunAsync(config, cancellationToken);
                var csvToken = downloads.Register(result.Report.CsvPath);
                var jsonToken = downloads.Register(result.Report.JsonPath);

                return Results.Json(new DiscoveryWebResponse
                {
                    GameName = result.Game.Name,
                    SearchRequestsExecuted = result.QueryPlan.Requests.Count,
                    CreatorLeadCount = result.Leads.Count,
                    CsvPath = result.Report.CsvPath,
                    JsonPath = result.Report.JsonPath,
                    CsvDownloadUrl = $"/api/download/{csvToken}",
                    JsonDownloadUrl = $"/api/download/{jsonToken}",
                    SeedGames = result.Game.SimilarGames,
                    Leads = result.Leads
                }, AppJson.Options);
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        });

        app.MapGet("/api/download/{token}", IResult (string token, DownloadRegistry downloads) =>
        {
            if (!downloads.TryResolve(token, out var filePath) || !File.Exists(filePath))
            {
                return Results.NotFound();
            }

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.File(filePath, contentType, Path.GetFileName(filePath), enableRangeProcessing: true);
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Console.WriteLine($"Local web app ready at {url}");
        });

        await app.RunAsync();
        return 0;
    }

    private static async Task<int> RunCliAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Config file not found: {configPath}");
                Console.Error.WriteLine("Pass --config <path> or open the local web app instead.");
                return 1;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<DiscoveryConfig>(json, AppJson.Options)
                ?? throw new InvalidOperationException($"Failed to parse config: {configPath}");

            var runner = new DiscoveryRunner();
            var result = await runner.RunAsync(config);

            Console.WriteLine($"Game: {result.Game.Name}");
            Console.WriteLine($"Search requests executed: {result.QueryPlan.Requests.Count}");
            Console.WriteLine($"Creator leads exported: {result.Leads.Count}");
            Console.WriteLine($"CSV: {result.Report.CsvPath}");
            Console.WriteLine($"JSON: {result.Report.JsonPath}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryGetConfigPath(string[] args, out string configPath)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = Path.GetFullPath(args[i + 1]);
                return true;
            }
        }

        configPath = string.Empty;
        return false;
    }

    private static string GetWebUrl(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return "http://127.0.0.1:5078";
    }

    private static string ResolveProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(current, "wwwroot")))
        {
            return current;
        }

        var nested = Path.Combine(current, "FindYouTubeCreator.App");
        if (Directory.Exists(Path.Combine(nested, "wwwroot")))
        {
            return nested;
        }

        return current;
    }
}
