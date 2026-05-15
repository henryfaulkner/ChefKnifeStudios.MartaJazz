<!--
Sync Impact Report:
- Version change: 1.0.0 → 2.0.0 (Major rewrite reflecting actual architecture)
- Modified principles: III rewritten (two-pass pipeline with spatial reconciliation), new VI added (GTFS ID mapping)
- Added sections: Solution Structure, Two-Pass Processing Pipeline, GTFS Data Pipeline, SignalR Event System
- Removed sections: Music Engine Flow (outdated naming), duplicate Event Message JSON (was identical to input)
- Restructured: Tech Stack & Architecture now reflects actual 11-project solution
- Templates requiring updates:
  - ✅ .specify/templates/plan-template.md (Constitution Check section exists)
  - ✅ .specify/templates/spec-template.md (scope/requirements alignment verified)
  - ✅ .specify/templates/tasks-template.md (task categorization reviewed)
- Follow-up TODOs: None
-->

# TransitJazz Application Constitution

## Core Principles

### I. Decoupled Cloud Architecture
The application MUST use a decoupled cloud architecture hosted on Azure. The system consists of three independently deployable units:
1. **Blazor WebAssembly frontend** — Azure Static Web App
2. **ASP.NET Core WebAPI** — hosts SignalR hub, serves GTFS static data, exposes REST endpoints
3. **TransitDataWorker** — background service that polls GTFS-RT, performs spatial reconciliation, publishes events via SignalR client

The frontend and backend communicate via SignalR (WSS) and HTTPS. The Worker connects to the WebAPI's SignalR hub as a client to publish events.

### II. No Frontend Secrets
The frontend MUST NEVER hold secrets. To authenticate with Azure Maps, the frontend MUST call the Azure Maps Auth Function to request a temporary token. All secrets (Client ID, Client Secret) MUST be stored securely in the Azure Function, not in client-side code.

### III. Two-Pass Real-Time Data Processing Pipeline
The TransitDataWorker MUST execute two processing passes per GTFS-RT polling cycle:

**V1 Pass (VehiclePositionBatchEvent)**: Maps raw GTFS-RT protobuf entities to `VehiclePositionBatchEvent` records. Emits cached positions as stale when fresh data is absent. Publishes via SignalR for basic vehicle position tracking.

**V2 Pass (RouteNearestPointBatchEvent)**: Spatial reconciliation — snaps each vehicle to the nearest point on its own route (keyed by `route_short_name` from GTFS static data, matching the GTFS-RT `Trip.RouteId`). Emits events for all active vehicles (moved, unchanged, and first-seen) so the frontend can animate vehicles along route geometries and maintain presence. Vehicles without a routeId or with an unknown routeId are skipped with diagnostic counters.

Vehicle state (nearest point, timestamp, route, speed, bearing) MUST be tracked in a `ConcurrentDictionary` for delta detection and stale vehicle pruning (20-minute threshold).

### IV. OpenTelemetry Observability
All .NET components MUST use structured logging to ensure correlation between background polling events, spatial reconciliation cycles, and SignalR broadcasts. OpenTelemetry integration with Azure Log Analytics Workspace is the target observability backend.

### V. Azure DevOps CI/CD Pipeline
Source control MUST be managed in GitHub. CI/CD pipelines MUST be managed via Azure DevOps. The build pipeline MUST produce two distinct artifacts: a compiled WASM artifact deployed to Azure Static Web Apps, and a Background Service Docker Image pushed to the Azure Container Registry (ACR).

### VI. GTFS ID Mapping
MARTA's GTFS static feed uses internal numeric IDs as `route_id` (e.g., `"26932"`), while the GTFS-RT feed's `Trip.RouteId` uses public-facing route short names (e.g., `"74"`, `"118"`). All systems that correlate static and real-time data MUST use `route_short_name` from `routes.txt` as the join key, falling back to `route_id` only when `route_short_name` is absent. The `RouteShapeProperties.RouteShortName` field carries this value through the shapes API.

## Tech Stack & Architecture

### Solution Structure (11 projects)

```
ChefKnifeStudios.TransitJazz.sln
│
├── Shared
│   └── ChefKnifeStudios.TransitJazz.Shared          # Models, events, geospatial utilities
│
├── Server
│   ├── ChefKnifeStudios.TransitJazz.Server.Core      # Domain interfaces, models (Ardalis.Result)
│   ├── ChefKnifeStudios.TransitJazz.Server.BL         # Business logic services
│   ├── ChefKnifeStudios.TransitJazz.Server.Infrastructure  # Data access (Redis, in-memory KV)
│   ├── ChefKnifeStudios.TransitJazz.Server.WebAPI     # ASP.NET Core API + SignalR hub
│   └── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker  # Background worker service
│
├── Client
│   ├── ChefKnifeStudios.TransitJazz.Client.Core       # Client services, config
│   ├── ChefKnifeStudios.TransitJazz.Client.Shared     # Blazor components (RCL)
│   └── ChefKnifeStudios.TransitJazz.Client.WebApp     # Blazor WASM app
│
├── Orchestration
│   ├── ChefKnifeStudios.TransitJazz.AppHost           # .NET Aspire orchestrator
│   └── ChefKnifeStudios.TransitJazz.ServiceDefaults   # Aspire service defaults, OTEL config
│
└── POC
    └── BusDataPoc/MartaJazz.Engine                    # Go-based early prototype (archived)
```

### Target Framework
- **.NET 10.0** for all projects
- **Blazor WebAssembly** for the frontend (MatBlazor for Material Design components)

### Frontend (Blazor WASM)
- Hosted as an Azure Static Web App
- Connects to WebAPI's SignalR hub to receive `EventEnvelope` batches
- Renders Azure Maps with vehicle animation along route geometries
- Calls Azure Maps Auth Function for temporary tokens (no direct secrets)
- Two rendering modes:
  - **V2 (primary)**: Animates vehicles along route polylines using `RouteNearestPointBatchEvent` records
  - **V1 (fallback)**: Plots raw vehicle positions when no nearest-point events arrive

### Backend — WebAPI
- ASP.NET Core Minimal API with endpoint groups
- Hosts `TransitHub` (SignalR hub) — receives batched events from the Worker and relays to clients
- **GTFS Static Data Pipeline**: `GtfsStaticLoader` (IHostedService) downloads the GTFS static zip at startup, parses `trips.txt`, `shapes.txt`, and `routes.txt`, builds `RouteShapeFeature` GeoJSON per route, stores in `IKeyValueRepository<string>` (in-memory)
- Exposes REST endpoints: route shapes (single + all), debug keys, maps auth token
- Uses Scalar for OpenAPI documentation

### Backend — TransitDataWorker
- .NET Worker Service (`BackgroundService`)
- Polls MARTA GTFS-RT protobuf feed (`vehiclepositions.pb`) on a configurable interval
- Connects to the WebAPI's SignalR hub as a **client** (not a host) via `SignalRHubPublisher`
- Maintains:
  - `_lastUpdateCache` — vehicle position delta detection for V1 events
  - `_vehicleStates` — nearest-point delta detection for V2 events
  - `_routeIndex` — `IReadOnlyDictionary<string, RoutePoint[]>` keyed by route short name
- Background tasks: stale vehicle pruning (5-min cycle), route index refresh (24-hour cycle)

### Shared Libraries
- `Shared/Events/` — SignalR event types (`EventEnvelope`, `ISignalREvent`, batch events)
- `Shared/Geospatial/` — `RoutePoint`, `HaversineCalculator`, `RouteSnapper` (nearest-point algorithm)
- `Shared/GtfsData/` — `RouteShapeFeature`, `RouteShapeGeometry`, `RouteShapeProperties`
- `Shared/EventData/` — event payload data models

### Security & Authentication
- Azure Maps: Azure Function handles Client ID & Secret securely, returning short-lived tokens to Blazor WASM app
- All inter-service communication (APIs, SignalR) MUST enforce standard secure transport (HTTPS/WSS)
- Microsoft Identity Web for Azure AD integration (WebAPI)

## Data Structures & Standards

### GTFS-RT Feed Entity (Input — Protobuf)
Deserialized from MARTA's `vehiclepositions.pb` via protobuf-net:
```json
{
  "id": "string",
  "vehicle": {
    "trip?": {
      "tripId": "string",
      "routeId?": "string (route_short_name, e.g. '74')",
      "startDate?": "string",
      "directionId?": "number"
    },
    "position?": {
      "latitude": "float",
      "longitude": "float",
      "bearing?": "float",
      "speed?": "float"
    },
    "timestamp": "number",
    "vehicle": {
      "id": "string",
      "label?": "string"
    }
  }
}
```

### SignalR Event System
All events flow through `EventEnvelope(EventType, Timestamp, Payload: ISignalREvent)`:

| Event Type | Purpose | Emitter |
|-----------|---------|---------|
| `VehiclePositionBatchEvent` | V1: raw vehicle positions with stale detection | Worker V1 pass |
| `RouteNearestPointBatchEvent` | V2: vehicle-to-route-point snapping with animation data | Worker V2 pass |
| `VehiclePositionUpdatedEvent` | Individual vehicle position update | (reserved) |
| `RouteAlertEvent` | GTFS-RT service alerts | (reserved) |
| `ArrivalPredictionEvent` | GTFS-RT trip updates / arrival predictions | (reserved) |
| `TripCompletedEvent` | Trip lifecycle events | (reserved) |
| `VehicleDepartedStopEvent` | Stop departure events | (reserved) |

### RouteShapeFeature (GeoJSON)
Stored in `IKeyValueRepository<string>` keyed by `route_id` from GTFS static:
```json
{
  "type": "Feature",
  "geometry": {
    "type": "LineString",
    "coordinates": [[lon, lat], ...]
  },
  "properties": {
    "routeId": "26932 (GTFS static route_id)",
    "routeShortName": "74 (public-facing, matches GTFS-RT Trip.RouteId)",
    "color": "#hexcolor",
    "textColor": "#hexcolor"
  }
}
```

### Observability Standards
- OpenTelemetry Integration: All components push OTEL data to Azure Log Analytics Workspace
- Structured logging throughout .NET components for correlation between background polling events and SignalR broadcasts
- Worker logs per-cycle spatial reconciliation metrics: moved, unchanged, skippedNoRouteId, skippedUnknownRoute

## Governance

### Amendment Procedure
- Constitution supersedes all other practices and documentation
- Amendments require documented changes with clear rationale
- Versioning follows semantic versioning (MAJOR.MINOR.PATCH):
  - MAJOR: Backward incompatible governance/principle removals or redefinitions
  - MINOR: New principle/section added or materially expanded guidance
  - PATCH: Clarifications, wording, typo fixes, non-semantic refinements

### Compliance Review
- All PRs/reviews MUST verify constitution compliance
- Complexity must be justified against core principles
- Deployment artifacts MUST match required outputs (WASM + Docker Image)

### Technology Enforcement
- Strict adherence to defined tech stack required
- No unauthorized technology substitutions without constitution amendment
- Azure services MUST be used as specified (Static Web Apps, Container Apps, Functions, Log Analytics, ACR)

**Version**: 2.0.0 | **Ratified**: 2026-05-03 | **Last Amended**: 2026-05-14
