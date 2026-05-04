using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public interface IPlayerConnectionTracker
{
    void Add(string playerId, string connectionId);
    void Remove(string playerId, string connectionId);
    IEnumerable<string> GetConnections(string playerId);
}

public class PlayerConnectionTracker : IPlayerConnectionTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connections = new();

    public ConcurrentDictionary<string, HashSet<string>> Connections => _connections;

    public void Add(string playerId, string connectionId)
    {
        Connections.AddOrUpdate(playerId,
            _ => new HashSet<string> { connectionId },
            (_, existing) =>
            {
                existing.Add(connectionId);
                return existing;
            });
    }

    public void Remove(string playerId, string connectionId)
    {
        if (Connections.TryGetValue(playerId, out var connections))
        {
            connections.Remove(connectionId);
            if (connections.Count == 0)
                Connections.TryRemove(playerId, out _);
        }
    }

    public IEnumerable<string> GetConnections(string playerId)
    {
        return Connections.TryGetValue(playerId, out var connections)
            ? connections
            : Enumerable.Empty<string>();
    }
}
