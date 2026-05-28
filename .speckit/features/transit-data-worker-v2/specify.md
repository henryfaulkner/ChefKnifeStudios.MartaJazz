# Specification: Transit Data Worker V2

## Feature Overview

**Feature Name:** transit-data-worker-v2  
**Target File:** `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Worker.cs`  
**Type:** Brownfield enhancement — replaces the existing V1 polling loop with a spatially-aware, delta-reporting pipeline.

---

## 1. User Stories

### US-001: Route Geometry Initialization
**As** the worker service,  
**I want** to load all route shape geometries into an in-memory spatial index at startup,  
**So that** each bus processing cycle can perform O(1) geographic filtering without hitting external APIs.

**Acceptance Criteria:**
- Route coordinates are fetched once at startup from the routes data source.
- Every `[lon, lat]` coordinate pair in each `RouteShapeFeature.Geometry.Coordinates` is encoded as a 5-character Geohash prefix.
- All encoded points are indexed into an `ILookup<string, RoutePoint>` keyed by the 5-character prefix.
- The index is automatically refreshed every 24 hours via a background timer.
- If the route fetch fails at startup, the service logs the error and retries rather than crashing.

---

### US-002: Per-Bus Spatial Candidate Selection
**As** the worker service,  
**I want** to narrow the set of route points considered for each bus to only those within the same geohash bucket,  
**So that** Haversine distance calculations are bounded to a small candidate set rather than the full route network.

**Acceptance Criteria:**
- Each bus's `Latitude`/`Longitude` (from `PositionData`) is encoded as a Geohash; the first 5 characters are used as the lookup key.
- The spatial index is queried with that key to retrieve candidate `RoutePoint` records.
- If no candidates exist for the bus's geohash bucket, the bus is skipped for that cycle (no event emitted, no state written).

---

### US-003: Nearest Route Point Calculation
**As** the worker service,  
**I want** to compute the exact distance from each bus to every candidate route point using the Haversine formula,  
**So that** the nearest route coordinate can be identified with geographic accuracy.

**Acceptance Criteria:**
- The Haversine formula is implemented using `System.Math` and `double` primitives (no external spatial libraries).
- The formula uses Earth radius = 6371 km.
- The nearest `RoutePoint` (minimum Haversine distance) is selected from the candidate set.
- The result carries: `RouteId`, `NearestLat`, `NearestLon`.

---

### US-004: Stateful Delta Detection
**As** the worker service,  
**I want** to track each vehicle's previous nearest route point in a `ConcurrentDictionary`,  
**So that** events are only emitted when a vehicle has moved to a different route point (delta-only reporting).

**Acceptance Criteria:**
- Vehicle state is stored as `VehicleState(NearestLat, NearestLon, LastUpdated, RouteId)` in a `ConcurrentDictionary<string, VehicleState>` keyed by `VehicleId`.
- On first poll for a vehicle, state is written but no event is emitted (warm-up cycle).
- On subsequent polls, an event is emitted only if `NearestLat` or `NearestLon` has changed from the stored state.
- If the incoming telemetry timestamp is older than `VehicleState.LastUpdated`, the update is discarded (out-of-order guard).

---

### US-005: Batched Event Dispatch
**As** the worker service,  
**I want** to collect all delta events from a single poll cycle into a batch and dispatch them in one call,  
**So that** the downstream API is not overwhelmed by per-vehicle individual requests.

**Acceptance Criteria:**
- Events for all moved vehicles in a cycle are accumulated in a `List<EventData>` before dispatch.
- The batch is sent as a single call to `transitHubPublisher.PublishBatchAsync(...)`.
- If the batch is empty, no dispatch call is made.
- Each event record contains: `VehicleId`, `RouteId`, prior state (`PriorNearestLat`, `PriorNearestLon`, `PriorUtcNow`), current state (`CurrentNearestLat`, `CurrentNearestLon`, `CurrentUtcNow`).

---

### US-006: Stale Vehicle Pruning
**As** the worker service,  
**I want** a background task to remove vehicle state entries that haven't been updated in 20 minutes,  
**So that** offline or removed vehicles don't cause unbounded memory growth.

**Acceptance Criteria:**
- A periodic background task runs independently of the main poll loop.
- Any `VehicleState` entry with `LastUpdated` older than 20 minutes is removed from the `ConcurrentDictionary`.
- The pruning task runs on its own interval (suggested: every 5 minutes).
- Pruning runs on the same `CancellationToken` as the main service; it stops cleanly on service shutdown.

---

## 2. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | The service MUST load route geometries into a geohash-keyed spatial index at startup. |
| FR-002 | The spatial index MUST use a 5-character Geohash prefix (~4.9 km² buckets). |
| FR-003 | The spatial index MUST refresh every 24 hours without service restart. |
| FR-004 | Each bus position MUST be geohash-encoded using `PositionData.Latitude` / `PositionData.Longitude`. |
| FR-005 | Distance calculations MUST use the Haversine formula with `System.Math` only. |
| FR-006 | Vehicle state MUST be stored in `ConcurrentDictionary<string, VehicleState>`. |
| FR-007 | Events MUST only be emitted when `NearestLat` or `NearestLon` changes (delta guard). |
| FR-008 | Out-of-order telemetry (older timestamp than stored state) MUST be discarded. |
| FR-009 | All events from one poll cycle MUST be dispatched as a single batch. |
| FR-010 | Vehicle state entries older than 20 minutes MUST be pruned by a background task. |
| FR-011 | The poll interval MUST be 10 seconds (down from V1's 15 seconds). |
| FR-012 | The service MUST continue operating if a single vehicle's processing throws an exception. |

---

## 3. Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | Haversine calculation MUST use `double` primitives to minimize GC pressure. |
| NFR-002 | The spatial index lookup MUST be O(1) per bus (dictionary key lookup). |
| NFR-003 | The `ConcurrentDictionary` MUST be thread-safe for concurrent reads/writes. |
| NFR-004 | Route geometry is treated as static reference data; buses are the high-frequency stream. |
| NFR-005 | The service MUST log meaningful diagnostics: batch sizes, pruned vehicles, index refreshes. |

---

## 4. Data Models

### 4.1 RoutePoint (new — spatial index entry)
```csharp
public record RoutePoint(
    string RouteId,
    double Lat,
    double Lon
);
```

### 4.2 VehicleState (new — short-term state store)
```csharp
public record VehicleState(
    double NearestLat,
    double NearestLon,
    DateTime LastUpdated,
    string RouteId
);
```

### 4.3 RouteNearestPointEvent (new — event payload added to Shared)
The existing `VehiclePositionBatchEvent` carries raw GTFS position data. V2 introduces a new event type for route-snapped positions:

```csharp
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

### 4.4 Spatial Index Type
```
ILookup<string, RoutePoint>
  Key   → 5-char Geohash prefix
  Value → RoutePoint(RouteId, Lat, Lon)
```

---

## 5. Success Criteria

| ID | Criterion |
|----|-----------|
| SC-001 | Spatial index is populated within 5 seconds of service startup under normal network conditions. |
| SC-002 | Per-poll CPU cost scales sub-linearly with total route network size (Geohash filtering is effective). |
| SC-003 | Zero events are emitted for vehicles that have not changed their nearest route point. |
| SC-004 | A vehicle that goes offline for 20+ minutes is removed from the state dictionary. |
| SC-005 | Out-of-order telemetry packets are silently discarded without corrupting state. |
| SC-006 | The service recovers from a failed route refresh without crashing (logs error, retains stale index). |

---

## 6. Edge Cases

| ID | Scenario | Expected Behavior |
|----|----------|-------------------|
| EC-001 | Bus position falls in a geohash bucket with no route points | Skip bus for this cycle; no event, no state write. |
| EC-002 | Two buses share the same `VehicleId` in one feed | Last-write-wins for state; both contribute to batch. |
| EC-003 | Route refresh returns empty list | Retain existing index; log warning. Do not clear index. |
| EC-004 | `PositionData.Latitude` / `Longitude` is 0.0, 0.0 (null island) | Geohash bucket likely empty; falls through EC-001 path. |
| EC-005 | Service restarts mid-cycle | State dict is empty; first poll is warm-up only (no events). |
| EC-006 | `transitHubPublisher.PublishBatchAsync` fails | Log error; do not retry in same cycle; state is already updated (next cycle will emit only new deltas). |

---

## 7. Integration Points

| Component | Role | Notes |
|-----------|------|-------|
| `RouteShapeFeature` (Shared) | Source of route geometry | `Geometry.Coordinates` is `double[][]` — note GeoJSON order: `[lon, lat]` |
| `PositionData` (Shared) | Source of bus coordinates | `Latitude` and `Longitude` are `float`; cast to `double` for Haversine |
| `ITransitHubPublisher` | Downstream event sink | Existing interface; V2 adds a new event type alongside existing batch |
| `VehiclePositionBatchEvent` | V1 event type | Remains in place; V2 adds `RouteNearestPointBatchEvent` as a parallel stream |
| Geohash library | Coordinate encoding | Add NuGet package (e.g., `Geohash-dotnet`) or implement inline |

---

## 8. Out of Scope

- Modifying `VehiclePositionBatchEvent` or any V1 event path.
- Persisting `VehicleState` to external storage (Redis, DB) — this is intentionally in-memory.
- GTFS-RT feed URL changes — stays at the existing MARTA endpoint.
- Authentication/authorization changes.
- Client-side (SignalR hub consumer) changes.
