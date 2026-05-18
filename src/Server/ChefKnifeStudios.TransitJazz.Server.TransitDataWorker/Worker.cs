using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
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
    ulong? _lastFeedHeaderTimestamp;
    readonly string _gtfsRtUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";
    readonly string _batchOutputDir = Path.Combine(AppContext.BaseDirectory, "event-batches");
    static readonly JsonSerializerOptions _batchJsonOptions = new() { WriteIndented = true };

    IReadOnlyDictionary<string, RoutePoint[]>? _routeIndex;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TransitDataWorker started.");

        await transitHubPublisher.StartAsync(stoppingToken);
        await InitializeRouteIndexAsync(stoppingToken);

        _ = Task.Run(() => PruneStaleVehicleStatesAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => RefreshRouteIndexAsync(stoppingToken), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var feed = await FetchGtfsRtFeedAsync(stoppingToken);
            if (feed != null)
            {
                await ProcessGtfsRtFeedAsync(feed, stoppingToken);

                if (_routeIndex != null)
                {
                    await ProcessSpatialReconciliationAsync(feed, stoppingToken);
                }
            }
        }
    }

    IReadOnlyDictionary<string, RoutePoint[]> BuildRouteIndex(List<RouteShapeFeature> shapes)
    {
        var routeGroups = new Dictionary<string, List<RoutePoint>>();

        foreach (var shape in shapes)
        {
            var key = shape.Properties.RouteShortName ?? shape.Properties.RouteId;
            if (!routeGroups.TryGetValue(key, out var points))
            {
                points = new List<RoutePoint>();
                routeGroups[key] = points;
            }

            foreach (var coord in shape.Geometry.Coordinates)
            {
                double lon = coord[0];
                double lat = coord[1];
                points.Add(new RoutePoint(key, lat, lon));
            }
        }

        return routeGroups.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
    }

    async Task InitializeRouteIndexAsync(CancellationToken ct)
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

                _routeIndex = BuildRouteIndex(shapes);
                sw.Stop();

                int routeCount = _routeIndex.Count;
                int totalPoints = _routeIndex.Values.Sum(pts => pts.Length);
                logger.LogInformation("Built route index: {RouteCount} routes, {TotalPoints} total points in {ElapsedMs}ms",
                    routeCount, totalPoints, sw.ElapsedMilliseconds);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize route index (attempt {Attempt}/{MaxRetries}).", attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
        }

        logger.LogWarning("Could not initialize route index after {MaxRetries} attempts. V2 reconciliation will be skipped until index is built.", maxRetries);
    }

    async Task ProcessSpatialReconciliationAsync(FeedMessage feed, CancellationToken ct)
    {
        try
        {
            var index = _routeIndex;
            if (index == null) return;

            var batch = new List<RouteNearestPointBatchEvent.RouteNearestPointRecord>();
            var debugBatch = new List<BatchDebugRecord>();
            int movedCount = 0, unchangedCount = 0, stationaryCount = 0, staleCount = 0, skippedNoRouteId = 0, skippedUnknownRoute = 0;

            foreach (var entity in feed.Entities)
            {
                try
                {
                    if (entity.Vehicle?.Position == null) continue;

                    string vehicleId = entity.Vehicle.Vehicle?.Id ?? entity.Id;
                    string? routeId = entity.Vehicle.Trip?.RouteId;

                    if (string.IsNullOrEmpty(routeId))
                    {
                        skippedNoRouteId++;
                        continue;
                    }

                    if (!index.TryGetValue(routeId, out var routePoints))
                    {
                        skippedUnknownRoute++;
                        continue;
                    }

                    double lat = (double)entity.Vehicle.Position.Latitude;
                    double lon = (double)entity.Vehicle.Position.Longitude;
                    var now = DateTime.UtcNow;

                    const int SnapWindowSize = 30;

                    var snap = _vehicleStates.TryGetValue(vehicleId, out var priorForSnap) && priorForSnap.RouteId == routeId
                        ? RouteSnapper.FindNearestInWindow(lat, lon, routePoints, priorForSnap.SnapIndex, SnapWindowSize)
                        : RouteSnapper.FindNearest(lat, lon, routePoints);
                    if (snap == null) continue;

                    var snapValue = snap.Value;
                    var nearest = snapValue.Point;
                    string outcome;
                    BatchDebugRecord debugRecord;

                    var currentVehicleTimestamp = entity.Vehicle.Timestamp;
                    bool isStale = false;

                    if (_vehicleStates.TryGetValue(vehicleId, out var prior))
                    {
                        if (prior.LastUpdated > now)
                        {
                            continue;
                        }

                        // Staleness check: the upstream GTFS-RT feed delivered the same
                        // per-vehicle sample as last poll. Emit a passthrough record so
                        // the client keeps extrapolating, but do not update _vehicleStates —
                        // we want the *next* fresh sample to compute its prior-delta against
                        // the last truly-new observation, not against this stale one.
                        isStale = currentVehicleTimestamp.HasValue
                            && prior.VehicleTimestamp.HasValue
                            && currentVehicleTimestamp.Value == prior.VehicleTimestamp.Value;

                        batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                            vehicleId,
                            nearest.RouteId,
                            prior.NearestLat,
                            prior.NearestLon,
                            prior.LastUpdated,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            isStale
                        ));

                        if (isStale)
                        {
                            outcome = "Stale";
                            staleCount++;
                        }
                        else if (prior.NearestLat != nearest.Lat || prior.NearestLon != nearest.Lon)
                        {
                            outcome = "Moved";
                            movedCount++;
                        }
                        else if ((entity.Vehicle.Position.Speed ?? 0f) == 0f)
                        {
                            outcome = "Stationary";
                            stationaryCount++;
                        }
                        else
                        {
                            outcome = "Unchanged";
                            unchangedCount++;
                        }

                        debugRecord = new BatchDebugRecord(
                            VehicleId: vehicleId,
                            RouteId: nearest.RouteId,
                            Outcome: outcome,
                            RawLat: lat,
                            RawLon: lon,
                            SnappedLat: nearest.Lat,
                            SnappedLon: nearest.Lon,
                            SnapDistanceKm: snapValue.DistanceKm,
                            SnapIndex: snapValue.Index,
                            RoutePointCount: routePoints.Length,
                            PriorRawLat: prior.LastRawLat,
                            PriorRawLon: prior.LastRawLon,
                            PriorSnappedLat: prior.NearestLat,
                            PriorSnappedLon: prior.NearestLon,
                            PriorSnapDistanceKm: prior.LastSnapDistanceKm,
                            PriorRouteId: prior.RouteId,
                            PriorObservationUtc: prior.LastUpdated,
                            ObservationUtc: now,
                            DeltaFromPriorSnapKm: HaversineCalculator.DistanceKm(prior.NearestLat, prior.NearestLon, nearest.Lat, nearest.Lon),
                            DeltaFromPriorRawKm: HaversineCalculator.DistanceKm(prior.LastRawLat, prior.LastRawLon, lat, lon),
                            SecondsSincePriorObservation: (now - prior.LastUpdated).TotalSeconds,
                            SpeedMetersPerSec: entity.Vehicle.Position.Speed,
                            Bearing: entity.Vehicle.Position.Bearing
                        );
                    }
                    else
                    {
                        batch.Add(new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                            vehicleId,
                            nearest.RouteId,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            false
                        ));
                        outcome = "FirstObservation";
                        movedCount++;

                        debugRecord = new BatchDebugRecord(
                            VehicleId: vehicleId,
                            RouteId: nearest.RouteId,
                            Outcome: outcome,
                            RawLat: lat,
                            RawLon: lon,
                            SnappedLat: nearest.Lat,
                            SnappedLon: nearest.Lon,
                            SnapDistanceKm: snapValue.DistanceKm,
                            SnapIndex: snapValue.Index,
                            RoutePointCount: routePoints.Length,
                            PriorRawLat: null,
                            PriorRawLon: null,
                            PriorSnappedLat: null,
                            PriorSnappedLon: null,
                            PriorSnapDistanceKm: null,
                            PriorRouteId: null,
                            PriorObservationUtc: null,
                            ObservationUtc: now,
                            DeltaFromPriorSnapKm: null,
                            DeltaFromPriorRawKm: null,
                            SecondsSincePriorObservation: null,
                            SpeedMetersPerSec: entity.Vehicle.Position.Speed,
                            Bearing: entity.Vehicle.Position.Bearing
                        );
                    }

                    debugBatch.Add(debugRecord);

                    // Skip _vehicleStates update for stale samples so the next fresh
                    // observation deltas against the last real data, not against a duplicate.
                    if (!isStale)
                    {
                        _vehicleStates[vehicleId] = new VehicleState(
                            nearest.Lat,
                            nearest.Lon,
                            now,
                            nearest.RouteId,
                            entity.Vehicle.Position.Speed,
                            entity.Vehicle.Position.Bearing,
                            snapValue.DistanceKm,
                            lat,
                            lon,
                            snapValue.Index,
                            currentVehicleTimestamp);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing spatial reconciliation for entity {EntityId}.", entity.Id);
                }
            }

            var feedTs = feed.Header?.Timestamp;
            var feedIsDuplicate = feedTs.HasValue && _lastFeedHeaderTimestamp.HasValue && feedTs.Value == _lastFeedHeaderTimestamp.Value;
            _lastFeedHeaderTimestamp = feedTs;

            logger.LogInformation(
                "Spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {Stationary} stationary, {Stale} stale, {SkippedNoRouteId} skippedNoRouteId, {SkippedUnknownRoute} skippedUnknownRoute. FeedHeaderTs={FeedHeaderTs} DuplicateFeed={DuplicateFeed}",
                movedCount, unchangedCount, stationaryCount, staleCount, skippedNoRouteId, skippedUnknownRoute, feedTs, feedIsDuplicate);

            if (batch.Count > 0)
            {
                await WriteBatchToDiskAsync(debugBatch, ct);

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

    async Task RefreshRouteIndexAsync(CancellationToken ct)
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

                var newIndex = BuildRouteIndex(shapes);
                _routeIndex = newIndex;

                int routeCount = newIndex.Count;
                int totalPoints = newIndex.Values.Sum(pts => pts.Length);
                logger.LogInformation("Refreshed route index: {RouteCount} routes, {TotalPoints} total points.",
                    routeCount, totalPoints);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh route index. Retaining existing index.");
            }
        }
    }

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

    async Task WriteBatchToDiskAsync(List<BatchDebugRecord> batch, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_batchOutputDir);
            var fileName = $"route-nearest-point-batch_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json";
            var filePath = Path.Combine(_batchOutputDir, fileName);

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, batch, _batchJsonOptions, ct);

            logger.LogInformation("Wrote spatial reconciliation batch to {FilePath} ({Count} records).", filePath, batch.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write spatial reconciliation batch to disk.");
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
