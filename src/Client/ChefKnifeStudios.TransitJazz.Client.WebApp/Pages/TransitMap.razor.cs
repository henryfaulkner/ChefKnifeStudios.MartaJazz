using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.WebApp.Pages;

public partial class TransitMap : ComponentBase, IDisposable
{
    [Inject] ISignalRNotificationService NotificationService { get; set; } = null!;
    [Inject] ILogger<TransitMap> Logger { get; set; } = null!;

    Map? _map;
    bool _mapReady;

    string _connectionLabel = "Connecting…";
    string _connectionCssClass = "connecting";

    static CameraOptions DefaultCameraOptions
        => new() { Center = new Position(33.749, -84.388), Zoom = 10 };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await NotificationService.InitAsync();
            _connectionLabel = "Connected";
            _connectionCssClass = "connected";
            NotificationService.NotificationReceived += HandleBatchAsync;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Failed to connect to SignalR hub");
            _connectionLabel = "Disconnected";
            _connectionCssClass = "disconnected";
        }
    }

    async Task OnMapReadyAsync(Map map)
    {
        _map = map;
        _mapReady = true;
        await Task.CompletedTask;
    }

    async Task HandleBatchAsync(List<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null) return;

        foreach (var envelope in batch)
        {
            if (envelope.Payload is not VehiclePositionUpdatedEvent evt) continue;
            if (evt.Position is null) continue;
            if (float.IsNaN(evt.Position.Latitude) || float.IsNaN(evt.Position.Longitude))
            {
                Logger.LogDebug("TransitMap: Skipping vehicle {VehicleId} — invalid coordinates", evt.Vehicle.Id);
                continue;
            }

            await _map.UpsertBusMarkerAsync(
                evt.Vehicle.Id,
                evt.Position.Latitude,
                evt.Position.Longitude);
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        NotificationService.NotificationReceived -= HandleBatchAsync;
    }
}
