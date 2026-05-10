using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

public class Worker(
    IHttpClientFactory httpClientFactory,
    ILogger<Worker> logger,
    ITransitHubPublisher transitHubPublisher) : BackgroundService
{
    readonly ConcurrentDictionary<string, VehiclePositionBatchEvent.VehiclePositionRecord> _lastUpdateCache = new();
    readonly ConcurrentDictionary<string, VehicleState> _vehicleStates = new();
    readonly string _gtfsRtUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

    ILookup<string, RoutePoint>? _routeSpatialIndex;

    /// <summary>
    /// Starts the worker loop: initializes the SignalR connection and spatial index,
    /// launches background maintenance tasks, then polls the GTFS-RT feed every 10 seconds.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TransitDataWorker started.");

        await transitHubPublisher.StartAsync(stoppingToken);
        await InitializeRouteSpatialIndexAsync(stoppingToken);

        _ = Task.Run(() => PruneStaleVehicleStatesAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => RefreshRouteSpatialIndexAsync(stoppingToken), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var feed = await FetchGtfsRtFeedAsync(stoppingToken);
            if (feed != null)
            {
                await ProcessGtfsRtFeedAsync(feed, stoppingToken);

                if (_routeSpatialIndex != null)
                {
                    await ProcessSpatialReconciliationAsync(feed, stoppingToken);
                }
            }
        }
    }

    /// <summary>
    /// Transforms route shape geometries into a geohash-keyed lookup for O(1) spatial candidate retrieval.
    /// </summary>
    /// <param name="shapes">Route shape features with GeoJSON coordinates in [lon, lat] order.</param>
    /// <returns>A lookup keyed by 5-character geohash prefix, containing all route points in that cell.</returns>
    ILookup<string, RoutePoint> BuildSpatialIndex(List<RouteShapeFeature> shapes)
    {
        var entries = new List<(string Hash, RoutePoint Point)>();

        foreach (var shape in shapes)
        {
            foreach (var coord in shape.Geometry.Coordinates)
            {
                // GeoJSON order: [lon, lat]
                double lon = coord[0];
                double lat = coord[1];
                string hash = GeohashEncoder.Encode(lat, lon, 5);
                entries.Add((hash, new RoutePoint(shape.Properties.RouteId, lat, lon)));
            }
        }

        return entries.ToLookup(x => x.Hash, x => x.Point);
    }

    /// <summary>
    /// Fetches route shapes from the WebAPI and builds the spatial index.
    /// Retries with exponential backoff up to 5 times on failure.
    /// </summary>
    async Task InitializeRouteSpatialIndexAsync(CancellationToken ct)
    {
        int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var client = httpClientFactory.CreateClient("RouteShapeApi");
                var response = await client.GetAsync("/gtfs/routes/shapes", ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var shapes = JsonSerializer.Deserialize<List<RouteShapeFeature>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (shapes == null || shapes.Count == 0)
                {
                    logger.LogWarning("Route shapes endpoint returned empty list. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                    continue;
                }

                _routeSpatialIndex = BuildSpatialIndex(shapes);
                sw.Stop();

                int bucketCount = _routeSpatialIndex.Count;
                int totalPoints = _routeSpatialIndex.Sum(g => g.Count());
                logger.LogInformation("Built spatial index: {BucketCount} buckets, {TotalPoints} total points in {ElapsedMs}ms",
                    bucketCount, totalPoints, sw.ElapsedMilliseconds);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize route spatial index (attempt {Attempt}/{MaxRetries}).", attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }

        logger.LogWarning("Could not initialize route spatial index after {MaxRetries} attempts. V2 reconciliation will be skipped until index is built.", maxRetries);
    }

    /// <summary>
    /// Finds the closest route point to a bus position using Haversine distance.
    /// </summary>
    /// <param name="busLat">Bus latitude in degrees.</param>
    /// <param name="busLon">Bus longitude in degrees.</param>
    /// <param name="candidates">Route points within the same geohash bucket.</param>
    /// <returns>The nearest route point, or null if candidates is empty.</returns>
    RoutePoint? FindNearestRoutePoint(double busLat, double busLon, IEnumerable<RoutePoint> candidates)
    {
        RoutePoint? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var candidate in candidates)
        {
            double distance = HaversineCalculator.DistanceKm(busLat, busLon, candidate.Lat, candidate.Lon);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// V2 processing pass: geohash-encodes each bus position, finds the nearest route point,
    /// and emits a batched event for vehicles that moved to a different route point since the last cycle.
    /// </summary>
    async Task ProcessSpatialReconciliationAsync(FeedMessage feed, CancellationToken ct)
    {
        try
        {
            var index = _routeSpatialIndex;
            if (index == null) return;

            var batch = new List<RouteNearestPointBatchEvent.RouteNearestPointRecord>();
            int movedCount = 0, unchangedCount = 0, skippedCount = 0;

            foreach (var entity in feed.Entities)
            {
                try
                {
                    if (entity.Vehicle?.Position == null) continue;

                    string vehicleId = entity.Vehicle.Vehicle?.Id ?? entity.Id;
                    double lat = (double)entity.Vehicle.Position.Latitude;
                    double lon = (double)entity.Vehicle.Position.Longitude;
                    var now = DateTime.UtcNow;

                    string prefix = GeohashEncoder.Encode(lat, lon, 5);
                    var candidates = index[prefix];

                    if (!candidates.Any())
                    {
                        skippedCount++;
                        continue;
                    }

                    var nearest = FindNearestRoutePoint(lat, lon, candidates);
                    if (nearest == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var nearestValue = nearest.Value;

                    if (_vehicleStates.TryGetValue(vehicleId, out var prior))
                    {
                        if (prior.LastUpdated > now)
                        {
                            continue;
                        }

                        if (prior.NearestLat != nearestValue.Lat || prior.NearestLon != nearestValue.Lon)
                        {
                            batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                                vehicleId,
                                nearestValue.RouteId,
                                prior.NearestLat,
                                prior.NearestLon,
                                prior.LastUpdated,
                                nearestValue.Lat,
                                nearestValue.Lon,
                                now,
                                entity.Vehicle.Position.Speed,
                                entity.Vehicle.Position.Bearing
                            ));
                            movedCount++;
                        }
                        else
                        {
                            unchangedCount++;
                        }
                    }

                    _vehicleStates[vehicleId] = new VehicleState(
                        nearestValue.Lat,
                        nearestValue.Lon,
                        now,
                        nearestValue.RouteId,
                        entity.Vehicle.Position.Speed,
                        entity.Vehicle.Position.Bearing);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing spatial reconciliation for entity {EntityId}.", entity.Id);
                }
            }

            logger.LogInformation("Spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {Skipped} skipped.",
                movedCount, unchangedCount, skippedCount);

            if (batch.Count > 0)
            {
                var envelope = new EventEnvelope(
                    nameof(RouteNearestPointBatchEvent),
                    DateTimeOffset.UtcNow,
                    new RouteNearestPointBatchEvent(batch)
                );

                var isBatchPublished = await transitHubPublisher.PublishBatchAsync(new List<EventEnvelope> { envelope }, ct);
                if (!isBatchPublished)
                {
                    logger.LogWarning("Failed to publish spatial reconciliation batch.");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in spatial reconciliation.");
        }
    }

    /// <summary>
    /// Background task that removes vehicle state entries not updated in 20+ minutes.
    /// Runs every 5 minutes to bound memory growth from offline vehicles.
    /// </summary>
    async Task PruneStaleVehicleStatesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                int pruned = 0;
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(20);

                foreach (var kvp in _vehicleStates)
                {
                    if (kvp.Value.LastUpdated < cutoff)
                    {
                        if (_vehicleStates.TryRemove(kvp.Key, out _))
                        {
                            pruned++;
                        }
                    }
                }

                logger.LogInformation("Pruned {PrunedCount} stale vehicle states, {RemainingCount} remaining.",
                    pruned, _vehicleStates.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error pruning stale vehicle states.");
            }
        }
    }

    /// <summary>
    /// Background task that rebuilds the route spatial index every 24 hours.
    /// Retains the existing index if the refresh fails or returns empty data.
    /// </summary>
    async Task RefreshRouteSpatialIndexAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var client = httpClientFactory.CreateClient("RouteShapeApi");
                var response = await client.GetAsync("/gtfs/routes/shapes", ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var shapes = JsonSerializer.Deserialize<List<RouteShapeFeature>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (shapes == null || shapes.Count == 0)
                {
                    logger.LogWarning("Route shapes refresh returned empty list. Retaining existing index.");
                    continue;
                }

                var newIndex = BuildSpatialIndex(shapes);
                _routeSpatialIndex = newIndex;

                int bucketCount = newIndex.Count;
                int totalPoints = newIndex.Sum(g => g.Count());
                logger.LogInformation("Refreshed spatial index: {BucketCount} buckets, {TotalPoints} total points.",
                    bucketCount, totalPoints);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh route spatial index. Retaining existing index.");
            }
        }
    }

    /// <summary>
    /// V1 processing pass: maps each GTFS-RT entity to a <see cref="VehiclePositionBatchEvent"/>
    /// and publishes the batch via SignalR. Emits cached positions as stale when fresh data is absent.
    /// </summary>
    async Task ProcessGtfsRtFeedAsync(FeedMessage feed, CancellationToken ct)
    {
        try
        {
            var batch = new List<EventEnvelope>();

            foreach (var entity in feed.Entities)
            {
                if (entity.Vehicle == null) continue;

                _lastUpdateCache.TryGetValue(entity.Id, out var cachedRecord);
                var records = new List<VehiclePositionBatchEvent.VehiclePositionRecord>();

                if (entity.Vehicle.Position == null)
                {
                    if (cachedRecord == null) continue;

                    records.Add(cachedRecord with { IsStale = true });
                }
                else
                {
                    if (cachedRecord != null)
                    {
                        records.Add(cachedRecord);
                    }

                    var vehicle = entity.Vehicle;
                    var vehicleData = EventMapper.ToVehicleData(vehicle.Vehicle!, vehicle);
                    var positionData = EventMapper.ToPositionData(vehicle.Position, vehicle);
                    var tripData = EventMapper.ToTripData(vehicle.Trip);

                    var currentRecord = new VehiclePositionBatchEvent.VehiclePositionRecord(vehicleData, positionData, tripData, IsStale: false);
                    records.Add(currentRecord);
                    _lastUpdateCache[entity.Id] = currentRecord;
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

    /// <summary>
    /// Downloads and deserializes the GTFS-RT vehicle positions protobuf feed from MARTA.
    /// </summary>
    /// <returns>The parsed feed, or null if the request failed.</returns>
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
