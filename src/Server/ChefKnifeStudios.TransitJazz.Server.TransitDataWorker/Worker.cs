using System.Collections.Concurrent;
using System.Net.Http.Headers;
using ChefKnifeStudios.TransitJazz.Shared.Events;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

public class Worker(
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger,
    ITransitHubPublisher transitHubPublisher) : BackgroundService
{
    readonly ConcurrentDictionary<string, (double Lat, double Lon)> _positionCache = new();
    readonly string _gtfsRtUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TransitDataWorker started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        await transitHubPublisher.StartAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessGtfsRtFeedAsync(stoppingToken);
        }
    }

    async Task ProcessGtfsRtFeedAsync(CancellationToken ct)
    {
        try
        {
            var feed = await FetchGtfsRtFeedAsync(ct);
            if (feed == null) return;

            var batch = new List<EventEnvelope>();

            foreach (var entity in feed.Entities)
            {
                if (entity.Vehicle?.Position == null) continue;

                var vehicle = entity.Vehicle;
                var vehicleId = vehicle.Vehicle?.Id ?? entity.Id;
                var lat = vehicle.Position.Latitude;
                var lon = vehicle.Position.Longitude;

                var isNewOrMoved = !_positionCache.TryGetValue(vehicleId, out var prev)
                    || prev.Lat != lat || prev.Lon != lon;

                if (!isNewOrMoved) continue;

                _positionCache[vehicleId] = (lat, lon);

                var evt = new VehiclePositionUpdatedEvent(
                    EventMapper.ToVehicleData(vehicle.Vehicle!, vehicle),
                    EventMapper.ToPositionData(vehicle.Position, vehicle),
                    EventMapper.ToTripData(vehicle.Trip)
                );

                batch.Add(new EventEnvelope(
                    nameof(VehiclePositionUpdatedEvent),
                    DateTimeOffset.UtcNow,
                    evt
                ));

                logger.LogInformation(
                    "Vehicle {VehicleId} updated: Lat {Lat}, Lon {Lon}, Speed {Speed}, Bearing {Bearing}",
                    vehicleId, lat, lon, vehicle.Position.Speed, vehicle.Position.Bearing);
            }

            logger.LogInformation("Processed GTFS-RT feed: {UpdatedCount} vehicles updated.", batch.Count);

            
            if (batch.Count > 0)
            {
                var isBatchPublished = await transitHubPublisher.PublishBatchAsync(batch, ct);

                if (isBatchPublished)
                {
                    logger.LogInformation("SignalR batch published: {Count} events.", batch.Count);
                }
                else
                {
                    logger.LogWarning("Failed to publish SignalR batch.");
                    logger.LogInformation("Attempting to reconnect to the SignalR hub.");
                    await transitHubPublisher.StartAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing GTFS-RT feed.");
        }
    }

    async Task<FeedMessage?> FetchGtfsRtFeedAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, _gtfsRtUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TransitJazz", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to fetch GTFS-RT feed: {StatusCode}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return ProtoBuf.Serializer.Deserialize<FeedMessage>(stream);
    }
}
