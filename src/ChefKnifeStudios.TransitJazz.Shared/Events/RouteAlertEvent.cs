using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record RouteAlertEvent(
    string FeedEntityId,
    AlertData Alert,
    bool IsActive
) : ISignalREvent;
