using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Http;
using ProtoBuf;
using TransitRealtime;

namespace MartaJazz.Engine;

public class Worker(
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Models.BusPosition> _positionCache = new();
    private const string MartaUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MARTA Jazz Engine POC (No-Auth) Started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunMusicalTick(stoppingToken);
        }
    }

    private async Task RunMusicalTick(CancellationToken ct)
    {
        try
        {
            var feed = await FetchMartaFeedAsync(ct);
            if (feed == null) return;

            int movedCount = 0;
            long timestamp = (long)feed.Header.Timestamp;

            foreach (var entity in feed.Entities)
            {
                if (entity.Vehicle?.Position == null) continue;

                var v = entity.Vehicle;
                var busId = v.Vehicle.Id;
                var lat = v.Position.Latitude;
                var lon = v.Position.Longitude;

                _positionCache.TryGetValue(busId, out var prev);

                if (prev == null || prev.Latitude != lat || prev.Longitude != lon)
                {
                    movedCount++;

                    var musicalEvent = new Models.EventMessage(
                        busId,
                        timestamp,
                        new Models.BusState(lat, lon, v.Position.Speed, v.Position.Bearing),
                        new Models.Coordinate(prev?.Latitude ?? lat, prev?.Longitude ?? lon),
                        15000
                    );

                    _positionCache[busId] = new Models.BusPosition(lat, lon);
                    LogMusicalEvent(musicalEvent);
                }
            }

            logger.LogInformation("Tick Complete: {MovedCount} buses moved.", movedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Engine Stalled");
        }
    }

    private void LogMusicalEvent(Models.EventMessage ev)
    {
        var speedText = ev.Current.Speed > 5 ? "Fast" : "Idle";
        Console.WriteLine($"[Bus {ev.BusId}] {speedText} | Lat: {ev.Current.Lat:F4}, Lon: {ev.Current.Lon:F4}");
    }

    private async Task<FeedMessage?> FetchMartaFeedAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, MartaUrl);
        request.Headers.Add("User-Agent", "MartaJazz/1.0 (Local POC)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return Serializer.Deserialize<FeedMessage>(stream);
    }
}
