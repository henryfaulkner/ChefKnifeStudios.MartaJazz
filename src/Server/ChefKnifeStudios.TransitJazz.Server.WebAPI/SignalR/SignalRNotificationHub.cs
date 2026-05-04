using ChefKnifeStudios.TransitJazz.Server.Core.Interfaces;
using ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public interface ISignalRNotificationClient
{
    Task ReceiveTransitJazzNotification(TransitJazzNotification notification, CancellationToken ct = default);
}

[AllowAnonymous]
public class SignalRNotificationHub : Hub<ISignalRNotificationClient>
{
    private readonly IPlayerConnectionTracker _connectionTracker;

    public SignalRNotificationHub(IPlayerConnectionTracker connectionTracker)
    {
        _connectionTracker = connectionTracker;
    }

    public override async Task OnConnectedAsync()
    {
        var playerId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(playerId))
        {
            _connectionTracker.Add(playerId, Context.ConnectionId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var playerId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(playerId))
        {
            _connectionTracker.Remove(playerId, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroupAsync(string groupId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"group-{groupId}");
    }

    public async Task LeaveGroupAsync(string groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group-{groupId}");
    }

    public async Task BroadcastNotification(TransitJazzNotification notification)
    {
        await Clients.All.ReceiveTransitJazzNotification(notification);
    }
}
