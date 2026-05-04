using ChefKnifeStudios.TransitJazz.Server.Core.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.SignalR;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public class TransitJazzNotificationHelper : ITransitJazzNotificationHelper
{
    private readonly IHubContext<SignalRNotificationHub, ISignalRNotificationClient> _hubContext;

    public TransitJazzNotificationHelper(IHubContext<SignalRNotificationHub, ISignalRNotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastToAllAsync(TransitJazzNotification notification, CancellationToken ct = default)
    {
        await _hubContext.Clients.All.ReceiveTransitJazzNotification(notification, ct);
    }

    public async Task BroadcastToGroupAsync(string groupId, TransitJazzNotification notification, CancellationToken ct = default)
    {
        await _hubContext.Clients.Group(GetGroupName(groupId)).ReceiveTransitJazzNotification(notification, ct);
    }

    public string GetGroupName(string id) => $"group-{id}";
}
