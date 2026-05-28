# Design: Real-Time Map Interpolation

## Overview

The client renders a near-real-time transit map by interpolating vehicle positions along route geometry between server polling intervals. The server pushes route-point transition events every ~10 seconds; the client smoothly animates each vehicle along the actual route path between its prior and current snapped coordinates.

---

## Data Flow

```
Server (10s poll)                          Client
─────────────────                          ──────
FetchGtfsRtFeed
  → ProcessSpatialReconciliation
    → RouteNearestPointBatchEvent ──SignalR──→ Receive batch
                                              For each vehicle:
                                                1. Look up route geometry (cached)
                                                2. Find path segment: prior → current
                                                3. Interpolate along path over time delta
                                                4. Extrapolate using speed/bearing if next event is late

GET /gtfs/routes/shapes ←────────────── Fetch once on page load (cached in memory)
```

---

## Client Responsibilities

### 1. Route Geometry Cache

The client fetches all route shapes once on page load via `GET /gtfs/routes/shapes`. This returns `RouteShapeFeature[]` with GeoJSON `LineString` coordinates per route. The client indexes these by `RouteId` for O(1) lookup.

- Geometry is static reference data; refresh only on page reload or after 24 hours.
- Store as ordered arrays of `[lon, lat]` per route for path-distance calculations.

### 2. Path-Following Interpolation

When a `RouteNearestPointRecord` arrives:

1. **Identify the route path**: Look up `RouteId` in the geometry cache.
2. **Locate prior point on path**: Find the coordinate in the route geometry closest to `(PriorNearestLat, PriorNearestLon)`. This gives the "start index" along the polyline.
3. **Locate current point on path**: Find the coordinate closest to `(CurrentNearestLat, CurrentNearestLon)`. This gives the "end index".
4. **Extract sub-path**: Slice the route coordinates from start index to end index. This is the ordered set of waypoints the vehicle traverses.
5. **Compute path length**: Sum the distances between consecutive waypoints in the sub-path.
6. **Animate over time delta**: `duration = CurrentUtcNow - PriorUtcNow`. Linearly interpolate position along the sub-path over this duration using cumulative distance fractions.

### 3. Velocity-Based Extrapolation

If the next event is late (> 10s since last update):

- Use `SpeedMetersPerSec` and `Bearing` from the last event to project the vehicle forward along the route path beyond `CurrentNearestLat/Lon`.
- Cap extrapolation at a reasonable distance (e.g., `speed * 5s`) to avoid overshooting.
- Snap extrapolated position back to the nearest route coordinate to prevent drift off-path.

### 4. Edge Cases

| Scenario | Behavior |
|----------|----------|
| Vehicle's first event (no prior) | Place at `CurrentNearestLat/Lon` instantly, no animation |
| Prior and current on different routes | Teleport to current position (route transfer) |
| Start index > end index (looping route) | Wrap around: path = [start→end of array] + [start of array→end] |
| Sub-path is a single point (same coord) | Vehicle is stationary; hold position |
| Speed is null | Use constant interpolation over time delta (no extrapolation) |
| No geometry found for RouteId | Fall back to straight-line lerp between prior/current |

---

## Event Payload Reference

```csharp
RouteNearestPointRecord(
    string VehicleId,
    string RouteId,
    double PriorNearestLat,
    double PriorNearestLon,
    DateTime PriorUtcNow,
    double CurrentNearestLat,
    double CurrentNearestLon,
    DateTime CurrentUtcNow,
    float? SpeedMetersPerSec,
    float? Bearing
)
```

All fields needed for interpolation are present in the event. The client does not need additional server calls per-vehicle.

---

## Performance Considerations

- **Route geometry size**: MARTA has ~100 routes. At ~500 coords per route, the full geometry cache is ~400KB uncompressed. Acceptable for a single fetch.
- **Point matching**: Finding the closest coordinate on a route polyline is O(N) per route. For ~500 points this is sub-millisecond. If needed, pre-compute a spatial index client-side.
- **Animation frame budget**: Interpolation math (cumulative distance fraction lookup) is trivial per vehicle. With ~200 active MARTA buses, this is well within 16ms frame budgets.
- **Memory**: One route geometry array + one `VehicleAnimationState` per active vehicle. Negligible.

---

## Dependencies

| Dependency | Status |
|------------|--------|
| `RouteNearestPointBatchEvent` with speed/bearing | Implemented (transit-data-worker-v2) |
| `GET /gtfs/routes/shapes` API endpoint | Exists |
| SignalR client connection | Exists |
| Map rendering library (Leaflet, Mapbox GL, etc.) | TBD — client tech choice |

---

## Out of Scope

- Server-side interpolation or prediction.
- Historical playback / time-scrubbing.
- Route-transfer animation (cross-fade between routes).
- Direction-of-travel indicators (arrows) — could be derived from bearing but is a separate UI feature.
