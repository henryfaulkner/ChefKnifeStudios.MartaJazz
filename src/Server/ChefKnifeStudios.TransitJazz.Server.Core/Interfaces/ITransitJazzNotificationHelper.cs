using ChefKnifeStudios.TransitJazz.Shared.DTOs.SignalR;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Server.Core.Interfaces;

public interface ITransitJazzNotificationHelper
{
    Task BroadcastToAllAsync(TransitJazzNotification notification, CancellationToken ct = default);
    Task BroadcastToGroupAsync(string groupId, TransitJazzNotification notification, CancellationToken ct = default);
    string GetGroupName(string id);
}
