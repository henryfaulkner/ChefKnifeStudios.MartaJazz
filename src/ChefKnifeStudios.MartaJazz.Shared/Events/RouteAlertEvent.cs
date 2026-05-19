using ChefKnifeStudios.MartaJazz.Shared.EventData;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record RouteAlertEvent(
    string FeedEntityId,
    AlertData Alert,
    bool IsActive
) : ISignalREvent;
