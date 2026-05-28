# Implementation Plan: Transit Data Worker V2

## 1. Architecture Overview

The V2 worker runs **alongside** the existing V1 loop inside the same `Worker` class. Both loops share the `ITransitHubPublisher` sink. The V1 loop continues emitting `VehiclePositionBatchEvent` from raw GTFS-RT telemetry; V2 adds a second processing pass that performs spatial reconciliation and emits `RouteNearestPointBatchEvent`.

### Rejected Alternative: Separate Worker Class
A second `BackgroundService` registered via `AddHostedService` would cleanly isolate V2, but it would require either duplicating the GTFS-RT fetch or sharing feed data between services. Keeping both loops in one class avoids that coordination overhead and lets V2 reuse the already-deserialized `FeedMessage`.

### Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│ Worker (BackgroundService)                                          │
│                                                                     │
│  ┌─ Startup ──────────────────────────────────────────────────────┐ │
│  │ InitializeRouteSpatialIndexAsync()                             │ │
│  │   → HTTP GET /gtfs/routes/shapes (WebAPI)                     │ │
│  │   → For each RouteShapeFeature:                               │ │
│  │       For each [lon, lat] in Geometry.Coordinates:            │ │
│  │         Encode Geohash → take prefix(5)                       │ │
│  │         Store (prefix → RoutePoint{RouteId, Lat, Lon})        │ │
│  │   → Build ILookup<string, RoutePoint>                         │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  ┌─ Main Loop (10s) ─────────────────────────────────────────────┐ │
│  │ 1. FetchGtfsRtFeedAsync()           ← shared with V1         │ │
│  │ 2. V1: ProcessGtfsRtFeedAsync()     → VehiclePositionBatch   │ │
│  │ 3. V2: ProcessSpatialReconciliation()                        │ │
│  │    For each entity with Position:                             │ │
│  │      a. Geohash bus lat/lon → prefix(5)                      │ │
│  │      b. Lookup candidates in RouteSpatialIndex                │ │
│  │      c. Haversine nearest from candidates                     │ │
│  │      d. Compare vs. _vehicleStates[vehicleId]                │ │
│  │      e. If moved → add to batch                              │ │
│  │    Dispatch batch → ITransitHubPublisher                      │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                     │
│  ┌─ Background Tasks ────────────────────────────────────────────┐ │
│  │ PruneStaleVehicleStatesAsync()  → every 5 min                │ │
│  │ RefreshRouteSpatialIndexAsync() → every 24 hours              │ │
│  └────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. Geohash Library Decision

**Choice: Inline implementation (no NuGet package).**

Rationale:
- The only operation needed is `Encode(lat, lon) → string`. No decode, bounding-box, or neighbor-cell operations are required at this stage.
- A ~40-line static method avoids a dependency for a single function.
- If neighbor-cell lookup is added later (to solve the boundary problem noted in the spec), we can swap to a NuGet package then.

The implementation encodes a lat/lon pair into a Base32 geohash string of configurable precision (default 12; we always call with `Substring(0, 5)` or pass precision=5 directly).

---

## 3. New Files

### 3.1 `RoutePoint.cs` (TransitDataWorker project)
```csharp
namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public readonly record struct RoutePoint(string RouteId, double Lat, double Lon);
```

Using `record struct` (value type) since these are small, immutable, and stored in bulk — avoids heap allocation per point.

### 3.2 `VehicleState.cs` (TransitDataWorker project)
```csharp
namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public record VehicleState(
    double NearestLat,
    double NearestLon,
    DateTime LastUpdated,
    string RouteId
);
```

Reference type (`record class`) since it's stored in a `ConcurrentDictionary` and updated by reference.

### 3.3 `GeohashEncoder.cs` (TransitDataWorker project)
Static utility class with `Encode(double lat, double lon, int precision = 12) → string`.

### 3.4 `HaversineCalculator.cs` (TransitDataWorker project)
Static utility class with `DistanceKm(double lat1, double lon1, double lat2, double lon2) → double`.

### 3.5 `RouteNearestPointBatchEvent.cs` (Shared project)
```csharp
namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record RouteNearestPointBatchEvent(
    IEnumerable<RouteNearestPointBatchEvent.RouteNearestPointRecord> BatchRecords
) : ISignalREvent
{
    public sealed record RouteNearestPointRecord(
        string VehicleId,
        string RouteId,
        double PriorNearestLat,
        double PriorNearestLon,
        DateTime PriorUtcNow,
        double CurrentNearestLat,
        double CurrentNearestLon,
        DateTime CurrentUtcNow
    );
}
```

---

## 4. Modified Files

### 4.1 `Worker.cs` — Major Changes

**New fields:**
```csharp
ILookup<string, RoutePoint> _routeSpatialIndex;
readonly ConcurrentDictionary<string, VehicleState> _vehicleStates = new();
```

**`ExecuteAsync` restructure:**
The current flow is:
1. Start SignalR → PeriodicTimer(15s) → ProcessGtfsRtFeedAsync

The new flow:
1. Start SignalR
2. `InitializeRouteSpatialIndexAsync()` — blocks until index is built
3. Fire-and-forget: `PruneStaleVehicleStatesAsync()` and `RefreshRouteSpatialIndexAsync()`
4. PeriodicTimer(10s) → `ProcessGtfsRtFeedAsync()` (V1, unchanged) → `ProcessSpatialReconciliationAsync(feed)` (V2, new)

**Key design detail:** The V1 `ProcessGtfsRtFeedAsync` currently calls `FetchGtfsRtFeedAsync` internally. We refactor so `ExecuteAsync` fetches the feed once per tick, then passes it to both V1 and V2 processors.

**New methods added to Worker:**

| Method | Responsibility |
|--------|---------------|
| `InitializeRouteSpatialIndexAsync()` | HTTP GET to WebAPI `/gtfs/routes/shapes`, transform to `ILookup` |
| `ProcessSpatialReconciliationAsync(FeedMessage feed)` | The V2 per-bus loop: geohash → candidates → haversine → delta → batch |
| `FindNearestRoutePoint(double busLat, double busLon, IEnumerable<RoutePoint> candidates)` | Returns the `RoutePoint` with minimum Haversine distance |
| `PruneStaleVehicleStatesAsync(CancellationToken ct)` | Removes entries older than 20 minutes, runs every 5 minutes |
| `RefreshRouteSpatialIndexAsync(CancellationToken ct)` | Rebuilds `_routeSpatialIndex` every 24 hours |
| `BuildSpatialIndex(List<RouteShapeFeature> shapes)` | Pure function: shapes → ILookup (shared by init and refresh) |

### 4.2 `Worker.cs` — V1 Refactor (minimal)

`ProcessGtfsRtFeedAsync` currently calls `FetchGtfsRtFeedAsync` internally. Change its signature to accept `FeedMessage?` as a parameter so the fetch happens once in `ExecuteAsync`.

Before:
```csharp
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    await ProcessGtfsRtFeedAsync(stoppingToken);
}
```

After:
```csharp
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    var feed = await FetchGtfsRtFeedAsync(stoppingToken);
    if (feed != null)
    {
        await ProcessGtfsRtFeedAsync(feed, stoppingToken);
        await ProcessSpatialReconciliationAsync(feed, stoppingToken);
    }
}
```

### 4.3 `Program.cs` — Add HttpClient configuration

Register a named `HttpClient` for route shape fetching with the WebAPI base URL:
```csharp
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["WebApi:BaseUrl"]!);
});
```

### 4.4 `appsettings.json` — Add WebAPI URL
```json
"WebApi": {
    "BaseUrl": "https://localhost:52834"
}
```

### 4.5 `ChefKnifeStudios.MartaJazz.Shared.csproj` — No changes expected
The Shared project already targets the correct framework.

### 4.6 `ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.csproj` — No NuGet additions
Geohash is inline; no new packages required.

---

## 5. Implementation Phases

### Phase 1: Data Models & Utilities (no behavior change)
1. Create `RoutePoint.cs`
2. Create `VehicleState.cs`
3. Create `GeohashEncoder.cs` with unit-testable static method
4. Create `HaversineCalculator.cs` with unit-testable static method
5. Create `RouteNearestPointBatchEvent.cs` in Shared

**Verification:** Project compiles. Existing tests pass.

### Phase 2: Route Spatial Index (startup path)
1. Add `WebApi:BaseUrl` to `appsettings.json` / `appsettings.Development.json`
2. Register named `HttpClient` in `Program.cs`
3. Add `BuildSpatialIndex()` to Worker
4. Add `InitializeRouteSpatialIndexAsync()` to Worker — call in `ExecuteAsync` before the loop
5. Add logging for index size and build time

**Verification:** Worker starts, logs "Built spatial index: {N} buckets, {M} total points in {T}ms".

### Phase 3: V1 Loop Refactor (minimal, non-breaking)
1. Change `ProcessGtfsRtFeedAsync` signature to accept `FeedMessage`
2. Move `FetchGtfsRtFeedAsync` call up to `ExecuteAsync`
3. Reduce PeriodicTimer from 15s to 10s

**Verification:** V1 behavior is unchanged — same events, same downstream output.

### Phase 4: Spatial Reconciliation Loop (V2 core)
1. Add `ProcessSpatialReconciliationAsync(FeedMessage, CancellationToken)` to Worker
2. Implement: geohash bus → lookup candidates → haversine nearest → delta check → batch collect
3. Wrap `EventEnvelope` with `RouteNearestPointBatchEvent` and dispatch via `PublishBatchAsync`
4. Add per-vehicle try/catch so one bad entity doesn't fail the batch (FR-012)
5. Add logging for batch size, skipped buses (no candidates)

**Verification:** Worker logs "Spatial reconciliation: {N} moved, {M} unchanged, {K} skipped". Events appear on SignalR hub.

### Phase 5: Background Tasks (memory management)
1. Add `PruneStaleVehicleStatesAsync` — loop with 5-minute `PeriodicTimer`, prune entries > 20 min
2. Add `RefreshRouteSpatialIndexAsync` — loop with 24-hour `PeriodicTimer`, rebuild index
3. Launch both as fire-and-forget `Task.Run` from `ExecuteAsync`, passing `stoppingToken`
4. Logging: "Pruned {N} stale vehicles", "Refreshed spatial index: {N} buckets"

**Verification:** Leave worker running > 20 min; stale entries are pruned. Simulate 24h by temporarily shortening the timer.

---

## 6. Geohash Boundary Problem — Deferred

The spec notes that buses near cell edges may miss the closest route point in an adjacent cell. The plan does **not** address this in the initial implementation. 

**Mitigation for V2.0:** The 5-character prefix covers ~4.9 km². Transit routes are dense enough that most buses will have candidates in their own cell. The risk is limited to buses at exact cell boundaries where the nearest route point is < ~2.5 km away but in the adjacent cell.

**V2.1 enhancement (if needed):** Query the 8 neighboring geohash cells in addition to the bus's own cell. This changes the lookup from 1 to 9 cells but keeps the overall complexity orders of magnitude below the naive O(N) scan. The `GeohashEncoder` would need a `Neighbors(string hash) → string[]` method.

---

## 7. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| WebAPI not running when worker starts | High | Retry with exponential backoff in `InitializeRouteSpatialIndexAsync`; V1 loop can run without the index |
| Route shapes endpoint returns empty | Medium | Guard: if empty, retain existing index (or log and wait for retry) |
| Haversine precision with `float→double` cast | Low | `float` has ~7 decimal digits; for lat/lon this is sub-meter — adequate for bus-to-route matching |
| Memory growth from stale vehicles | Medium | Pruning task removes entries > 20 min; logged for monitoring |
| GeoJSON `[lon, lat]` vs. `[lat, lon]` confusion | High | Explicitly document and name variables: coordinates[i][0] = lon, coordinates[i][1] = lat |

---

## 8. Testing Strategy

### Unit Tests (new test project or existing)
- `GeohashEncoder.Encode` — known input/output pairs (e.g., MARTA Five Points station → expected geohash)
- `HaversineCalculator.DistanceKm` — verify against known distances (e.g., Atlanta to Decatur ≈ 9.7 km)
- `BuildSpatialIndex` — verify correct bucketing, correct coordinate order, empty input

### Integration Tests
- Full `ProcessSpatialReconciliationAsync` with a mock feed and a small pre-built index
- Delta detection: run two cycles, verify second cycle emits events only for moved buses
- Out-of-order: inject older timestamp, verify discard

### Manual Verification
- Run worker locally against MARTA's live GTFS-RT feed
- Observe SignalR output for `RouteNearestPointBatchEvent` payloads
- Verify coordinates visually on a map (spot-check a few VehicleIds)
