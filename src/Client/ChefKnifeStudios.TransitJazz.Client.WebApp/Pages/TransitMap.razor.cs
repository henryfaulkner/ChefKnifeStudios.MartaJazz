using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

public partial class TransitMap : ComponentBase, IDisposable
{
    [Inject] ISignalRNotificationService NotificationService { get; set; } = null!;
    [Inject] ILogger<TransitMap> Logger { get; set; } = null!;
    [Inject] IGtfsEndpointsService GtfsEndpointsService { get; set; } = null!;

    Map? _map;
    bool _mapReady;
    IEnumerable<EventEnvelope>? _pendingBatch;


    string _connectionLabel = "Connecting…";
    string _connectionCssClass = "connecting";

    // vehicleId → routeId, updated on every VehiclePositionUpdatedEvent
    readonly Dictionary<string, string> _vehicleRouteMap = new();

    // routeId → GeoJSON string (client-side cache, lives for page lifetime)
    readonly Dictionary<string, RouteShapeFeature> _routeShapeCache = new();

    static CameraOptions DefaultCameraOptions
        => new() { Center = new Position(33.749, -84.388), Zoom = 10 };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await NotificationService.InitAsync();
            _connectionLabel = "Connected";
            _connectionCssClass = "connected";
            NotificationService.NotificationReceived += HandleVehicleBatchAsync;

            await LoadRoutesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Failed to connect to SignalR hub");
            _connectionLabel = "Disconnected";
            _connectionCssClass = "disconnected";
        }
    }

    public void Dispose()
    {
        NotificationService.NotificationReceived -= HandleVehicleBatchAsync;
    }

    async Task OnMapReadyAsync(Map map)
    {
        _map = map;
        _mapReady = true;

        Logger.LogDebug("TransitMap: pushing {Count} route geometries to animator and map layer", _routeShapeCache.Count);
        foreach (var (routeId, feature) in _routeShapeCache)
        {
            await _map.AddRouteShapeFeatureAsync(routeId, feature.Geometry.Coordinates, feature.Properties.Color);
            await _map.LoadRouteGeometryForAnimationAsync(routeId, feature.Geometry.Coordinates);
        }
        Logger.LogDebug("TransitMap: route geometry push complete");

        if (_pendingBatch is not null)
        {
            var batch = _pendingBatch;
            _pendingBatch = null;
            await HandleVehicleBatchAsync(batch);
        }
    }

    async Task HandleVehicleBatchAsync(IEnumerable<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null)
        {
            _pendingBatch = batch;
            return;
        }

        // Handle RouteNearestPointBatchEvent — animated path-following
        var nearestPointRecords = batch
            .Where(x => x.Payload is RouteNearestPointBatchEvent)
            .Select(x => (RouteNearestPointBatchEvent)x.Payload)
            .SelectMany(e => e.BatchRecords)
            .ToArray();

        Logger.LogDebug("TransitMap: batch contains {NearestCount} nearest-point records, {PosCount} position events",
            nearestPointRecords.Length,
            batch.Count(x => x.Payload is VehiclePositionUpdatedEvent));

        if (nearestPointRecords.Length > 0)
        {
            var records = nearestPointRecords.Select(r => (object)new
            {
                vehicleId = r.VehicleId,
                routeId = r.RouteId,
                priorLon = r.PriorNearestLon,
                priorLat = r.PriorNearestLat,
                currentLon = r.CurrentNearestLon,
                currentLat = r.CurrentNearestLat,
                durationMs = (r.CurrentUtcNow - r.PriorUtcNow).TotalMilliseconds,
                speed = r.SpeedMetersPerSec,
                bearing = r.Bearing
            }).ToArray();

            Logger.LogDebug("TransitMap: forwarding {Count} nearest-point records to animator", records.Length);
            await _map.ProcessNearestPointBatchAsync(records);
        }

        // V1 fallback: only plot raw positions when no nearest-point events arrived this batch.
        // If the animator is active, PlotVehiclesAsync clears the datasource and wipes animator-managed features.
        if (nearestPointRecords.Length == 0)
        {
            var payloadBatch = batch
                .Where(x => x.Payload is VehiclePositionUpdatedEvent)
                .Select(x => x.Payload as VehiclePositionUpdatedEvent);

            var featureCollection = new
            {
                type = "FeatureCollection",
                features = payloadBatch
                    .Select(x => new
                    {
                        type = "Feature",
                        id = $"vehicle-{x.Vehicle.Id}",
                        properties = new
                        {
                            vehicleId = x.Vehicle.Id,
                            vehicleName = x.Vehicle.LicensePlate,
                            pinIcon = "stop-pin-green",
                        },
                        geometry = new
                        {
                            type = "Point",
                            coordinates = new[] { x.Position.Longitude, x.Position.Latitude }
                        }
                    }).ToArray(),
            };

            foreach (var payload in payloadBatch)
            {
                if (payload.Position is null) continue;
                if (float.IsNaN(payload.Position.Latitude) || float.IsNaN(payload.Position.Longitude))
                {
                    Logger.LogDebug("TransitMap: Skipping vehicle {VehicleId} — invalid coordinates", payload.Vehicle.Id);
                    continue;
                }

                if (payload.Trip?.RouteId is { } routeId)
                    _vehicleRouteMap[payload.Vehicle.Id] = routeId;
            }

            Logger.LogDebug("TransitMap: no nearest-point records, falling back to V1 plot");
            await _map.PlotVehiclesAsync(featureCollection, true);
        }

        await InvokeAsync(StateHasChanged);
    }

    async Task OnVehicleMarkerClickedAsync((Map Map, string VehicleId) args)
    {
        return;
    }

    async Task OnMapBodyClickedAsync(Map map)
    {
        return;
    }

    async Task LoadRoutesAsync(CancellationToken ct = default)
    {
        var res = await GtfsEndpointsService.GetAllRouteShapes(ct);
        if (!res.IsSuccess)
        {
            Logger.LogError("Unable to pull route data.");
            return;
        }

        _routeShapeCache.Clear();
        foreach (var routeShapeFeature in res.Value)
        {
            _routeShapeCache[routeShapeFeature.Properties.RouteShortName] = routeShapeFeature;
        }
    }
}
