using Meilisearch;
using SearchService.Data;

namespace SearchService.Services;

/// <summary>
/// Loads the search dictionary (makes, models, aliases, part types, synonyms) from AuctionService at startup and every
/// 10 minutes, and gives Meilisearch the same synonyms (hood ↔ bonnet, tire ↔ tyre ...). If AuctionService isn't up
/// yet, search still works – just without understanding those words – and the next refresh tries again.
/// </summary>
public class DictionaryRefresher(IHttpClientFactory httpFactory, IConfiguration config, SearchDictionaryProvider provider,
    MeilisearchClient client, ILogger<DictionaryRefresher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            var wait = provider.LoadedAt is null ? TimeSpan.FromSeconds(15) : Interval;
            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient(nameof(DictionaryRefresher));
            var data = await http.GetFromJsonAsync<DictionaryData>($"{config["AuctionSvcUrl"]}/api/catalog/dictionary", ct);
            if (data is null) return;

            provider.Set(data);
            var task = await client.Auctions().UpdateSynonymsAsync(provider.Current.MeiliSynonyms(), ct);
            await client.WaitForTaskAsync(task.TaskUid, cancellationToken: ct);
            logger.LogInformation("Search dictionary loaded: {Makes} makes, {PartTypes} part types",
                data.Makes.Count, data.PartTypes.Count);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or MeilisearchApiError or System.Text.Json.JsonException)
        {
            logger.LogWarning("Could not load the search dictionary yet: {Message}", e.Message);
        }
    }
}
