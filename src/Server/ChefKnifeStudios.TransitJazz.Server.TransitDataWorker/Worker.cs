using System.Collections.Concurrent;
using System.Net.Http.Headers;
using ChefKnifeStudios.TransitJazz.Shared.EventData;
using ChefKnifeStudios.TransitJazz.Shared.Events;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

public class Worker(
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger,
    ITransitHubPublisher transitHubPublisher) : BackgroundService
{
    readonly ConcurrentDictionary<string, (VehicleData, PositionData, TripData?)> _lastUpdateCache = new();
    readonly string _gtfsRtUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TransitDataWorker started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
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
                if (entity.Vehicle == null) continue;

                var lastUpdateRecord = _lastUpdateCache.GetValueOrDefault(entity.Id);
                var records = new List<VehiclePositionBatchEvent.VehiclePositionRecord>();

                if (entity.Vehicle.Position == null)
                {
                    if (lastUpdateRecord == default) continue; // No position data and no cache — skip

                    // No fresh position — publish cached position as stale so client can count stale cycles
                    var (cachedVehicle, cachedPosition, cachedTrip) = lastUpdateRecord;
                    records.Add(new VehiclePositionBatchEvent.VehiclePositionRecord(cachedVehicle, cachedPosition, cachedTrip, IsStale: true));
                }
                else
                {
                    // Add prior available data
                    if (lastUpdateRecord != default)
                    {
                        var (priorVehicle, priorPosition, priorTrip) = lastUpdateRecord;
                        records.Add(new VehiclePositionBatchEvent.VehiclePositionRecord(priorVehicle, priorPosition, priorTrip, IsStale: false));
                    }

                    // Add current available data
                    var vehicle = entity.Vehicle;
                    var vehicleData = EventMapper.ToVehicleData(vehicle.Vehicle!, vehicle);
                    var positionData = EventMapper.ToPositionData(vehicle.Position, vehicle);
                    var tripData = EventMapper.ToTripData(vehicle.Trip);

                    records.Add(new VehiclePositionBatchEvent.VehiclePositionRecord(vehicleData, positionData, tripData, IsStale: false));

                    var didUpdate = _lastUpdateCache.TryAdd(entity.Id, (vehicleData, positionData, tripData));
                    if (!didUpdate) logger.LogWarning("Failed to update cache for vehicle {VehicleId}.", entity.Id);
                }

                batch.Add(new EventEnvelope(
                    nameof(VehiclePositionBatchEvent),
                    DateTimeOffset.UtcNow,
                    new VehiclePositionBatchEvent(records)
                ));
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
