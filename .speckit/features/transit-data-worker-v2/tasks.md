# Tasks: Transit Data Worker V2

## Phase 1: Data Models & Utilities

### Task 1.1: Create `RoutePoint` record struct
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/RoutePoint.cs` (new)
- **Action:** Create `readonly record struct RoutePoint(string RouteId, double Lat, double Lon)`
- **Spec refs:** US-001, FR-001
- **Status:** [X] Complete

### Task 1.2: Create `VehicleState` record
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/VehicleState.cs` (new)
- **Action:** Create `record VehicleState(double NearestLat, double NearestLon, DateTime LastUpdated, string RouteId)`
- **Spec refs:** US-004, FR-006
- **Status:** [X] Complete

### Task 1.3: Create `GeohashEncoder` static utility
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/GeohashEncoder.cs` (new)
- **Action:** Implement `static string Encode(double lat, double lon, int precision = 5)` using Base32 interleaved bit encoding. No external dependencies.
- **Spec refs:** US-001, US-002, FR-002
- **Status:** [X] Complete

### Task 1.4: Create `HaversineCalculator` static utility
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/HaversineCalculator.cs` (new)
- **Action:** Implement `static double DistanceKm(double lat1, double lon1, double lat2, double lon2)` using Earth radius 6371 km. Use `System.Math` and `double` only.
- **Spec refs:** US-003, FR-005, NFR-001
- **Status:** [X] Complete

### Task 1.5: Create `RouteNearestPointBatchEvent` in Shared
- **File:** `src/ChefKnifeStudios.MartaJazz.Shared/Events/RouteNearestPointBatchEvent.cs` (new)
- **Action:** Create sealed record implementing `ISignalREvent` with nested `RouteNearestPointRecord(VehicleId, RouteId, PriorNearestLat, PriorNearestLon, PriorUtcNow, CurrentNearestLat, CurrentNearestLon, CurrentUtcNow)`.
- **Spec refs:** US-005, FR-009
- **Status:** [X] Complete

### Phase 1 Verification
- [X] Solution compiles with no errors
- [X] Existing tests pass (no behavior change)

---

## Phase 2: Route Spatial Index

### Task 2.1: Add `WebApi:BaseUrl` to appsettings
- **Files:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json`, `appsettings.Development.json`
- **Action:** Add `"WebApi": { "BaseUrl": "https://localhost:52834" }` section.
- **Status:** [X] Complete

### Task 2.2: Register named HttpClient in `Program.cs`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Program.cs`
- **Action:** Add `builder.Services.AddHttpClient("RouteShapeApi", ...)` with `BaseAddress` from config.
- **Status:** [X] Complete

### Task 2.3: Implement `BuildSpatialIndex` in Worker
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Add method `ILookup<string, RoutePoint> BuildSpatialIndex(List<RouteShapeFeature> shapes)`. For each shape, iterate `Geometry.Coordinates` (GeoJSON `[lon, lat]` order — index `[0]` = lon, `[1]` = lat). Encode each point via `GeohashEncoder.Encode(lat, lon, 5)`. Return `ToLookup(x => x.Hash, x => x.Point)`.
- **Spec refs:** US-001, FR-001, FR-002
- **Critical:** Coordinate order — `coordinates[i][0]` is longitude, `coordinates[i][1]` is latitude.
- **Status:** [X] Complete

### Task 2.4: Implement `InitializeRouteSpatialIndexAsync` in Worker
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** HTTP GET to `/gtfs/routes/shapes` using named `HttpClient("RouteShapeApi")`. Deserialize to `List<RouteShapeFeature>`. Call `BuildSpatialIndex`. Assign to `_routeSpatialIndex` field. Retry with exponential backoff on failure (max 5 retries). Log index stats: bucket count, total point count, elapsed time.
- **Spec refs:** US-001, FR-001, SC-001, SC-006
- **Status:** [X] Complete

### Task 2.5: Wire index initialization into `ExecuteAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Call `await InitializeRouteSpatialIndexAsync()` after `transitHubPublisher.StartAsync()` and before the main loop. Add `_routeSpatialIndex` field declaration.
- **Status:** [X] Complete

### Phase 2 Verification
- [X] Worker starts and logs: "Built spatial index: {N} buckets, {M} total points in {T}ms"
- [X] V1 behavior unchanged

---

## Phase 3: V1 Loop Refactor

### Task 3.1: Refactor `ProcessGtfsRtFeedAsync` to accept `FeedMessage`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Change signature from `ProcessGtfsRtFeedAsync(CancellationToken ct)` to `ProcessGtfsRtFeedAsync(FeedMessage feed, CancellationToken ct)`. Remove the internal `FetchGtfsRtFeedAsync` call and the `if (feed == null) return;` guard — the null check moves up to `ExecuteAsync`.
- **Status:** [X] Complete

### Task 3.2: Restructure `ExecuteAsync` main loop
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Change loop body to: `var feed = await FetchGtfsRtFeedAsync(stoppingToken); if (feed != null) { await ProcessGtfsRtFeedAsync(feed, stoppingToken); }`. Change `PeriodicTimer` from 15s to 10s.
- **Spec refs:** FR-011
- **Status:** [X] Complete

### Phase 3 Verification
- [X] V1 events still publish correctly via SignalR
- [X] Poll interval is 10 seconds

---

## Phase 4: Spatial Reconciliation (V2 Core)

### Task 4.1: Add `_vehicleStates` field to Worker
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Add field `readonly ConcurrentDictionary<string, VehicleState> _vehicleStates = new();`
- **Spec refs:** FR-006, NFR-003
- **Status:** [X] Complete

### Task 4.2: Implement `FindNearestRoutePoint`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Add method `RoutePoint? FindNearestRoutePoint(double busLat, double busLon, IEnumerable<RoutePoint> candidates)`. Iterate candidates, compute `HaversineCalculator.DistanceKm`, track minimum. Return `null` if candidates is empty.
- **Spec refs:** US-003, FR-005
- **Status:** [X] Complete

### Task 4.3: Implement `ProcessSpatialReconciliationAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** New method `async Task ProcessSpatialReconciliationAsync(FeedMessage feed, CancellationToken ct)`. For each `FeedEntity` with a non-null `Vehicle.Position`:
  1. Extract `vehicleId` from `entity.Vehicle.Vehicle?.Id ?? entity.Id`
  2. Cast `Position.Latitude`/`Longitude` (float) to double
  3. Encode geohash prefix: `GeohashEncoder.Encode(lat, lon, 5)`
  4. Lookup candidates: `_routeSpatialIndex[prefix]`
  5. If no candidates → skip (log as "skipped"), continue
  6. `FindNearestRoutePoint(lat, lon, candidates)` → nearest
  7. Check `_vehicleStates.TryGetValue(vehicleId, out var prior)`:
     - If prior exists AND prior.LastUpdated > current timestamp → discard (out-of-order guard, FR-008)
     - If prior exists AND (prior.NearestLat != nearest.Lat || prior.NearestLon != nearest.Lon) → add `RouteNearestPointRecord` to batch
     - If no prior → warm-up, no event (US-004)
  8. Update `_vehicleStates[vehicleId]` with new `VehicleState`
  9. Wrap each entity processing in try/catch to isolate failures (FR-012)
  After loop: if batch non-empty, wrap in `EventEnvelope(nameof(RouteNearestPointBatchEvent), ...)` and `PublishBatchAsync`. Log moved/unchanged/skipped counts.
- **Spec refs:** US-002, US-003, US-004, US-005, FR-004, FR-007, FR-008, FR-009, FR-012, EC-001, EC-005, EC-006
- **Status:** [X] Complete

### Task 4.4: Wire V2 into `ExecuteAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** Add `await ProcessSpatialReconciliationAsync(feed, stoppingToken);` after the V1 call in the main loop. Only call if `_routeSpatialIndex != null`.
- **Status:** [X] Complete

### Phase 4 Verification
- [X] Worker logs "Spatial reconciliation: {N} moved, {M} unchanged, {K} skipped"
- [X] `RouteNearestPointBatchEvent` envelopes appear on SignalR hub
- [X] First poll cycle emits no events (warm-up)
- [X] Subsequent cycles only emit for vehicles that moved to a new route point (SC-003)

---

## Phase 5: Background Tasks

### Task 5.1: Implement `PruneStaleVehicleStatesAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** New method `async Task PruneStaleVehicleStatesAsync(CancellationToken ct)`. Loop with `PeriodicTimer(TimeSpan.FromMinutes(5))`. On each tick, iterate `_vehicleStates` snapshot, remove entries where `DateTime.UtcNow - entry.Value.LastUpdated > TimeSpan.FromMinutes(20)`. Log: "Pruned {N} stale vehicle states, {M} remaining".
- **Spec refs:** US-006, FR-010, SC-004
- **Status:** [X] Complete

### Task 5.2: Implement `RefreshRouteSpatialIndexAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** New method `async Task RefreshRouteSpatialIndexAsync(CancellationToken ct)`. Loop with `PeriodicTimer(TimeSpan.FromHours(24))`. On each tick, fetch route shapes, call `BuildSpatialIndex`, and atomically replace `_routeSpatialIndex`. If fetch/build fails, log error and retain existing index (EC-003, SC-006). Log: "Refreshed spatial index: {N} buckets, {M} total points".
- **Spec refs:** US-001 (refresh), FR-003, EC-003, SC-006
- **Status:** [X] Complete

### Task 5.3: Launch background tasks from `ExecuteAsync`
- **File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`
- **Action:** After `InitializeRouteSpatialIndexAsync` and before the main loop, launch both background tasks: `_ = Task.Run(() => PruneStaleVehicleStatesAsync(stoppingToken))` and `_ = Task.Run(() => RefreshRouteSpatialIndexAsync(stoppingToken))`.
- **Status:** [X] Complete

### Phase 5 Verification
- [X] After 20+ minutes, stale vehicle entries are pruned (SC-004)
- [X] Route index refresh logs appear (manually test by shortening the 24h timer)
- [X] Both tasks stop cleanly on service shutdown

---

## Summary

| Phase | Tasks | Spec Coverage |
|-------|-------|---------------|
| 1. Data Models & Utilities | 5 tasks | FR-001, FR-002, FR-005, FR-006, FR-009, NFR-001 |
| 2. Route Spatial Index | 5 tasks | FR-001, FR-002, SC-001, SC-006 |
| 3. V1 Loop Refactor | 2 tasks | FR-011 |
| 4. Spatial Reconciliation | 4 tasks | FR-004, FR-007, FR-008, FR-009, FR-012, SC-003, SC-005 |
| 5. Background Tasks | 3 tasks | FR-003, FR-010, SC-004, SC-006 |
| **Total** | **19 tasks** | **All 12 FRs, 5 NFRs, 6 SCs, 6 ECs covered** |
