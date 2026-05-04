using Microsoft.AspNetCore.SignalR;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public class PlayerIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.GetHttpContext()?.Request.Query["playerId"].FirstOrDefault();
    }
}
