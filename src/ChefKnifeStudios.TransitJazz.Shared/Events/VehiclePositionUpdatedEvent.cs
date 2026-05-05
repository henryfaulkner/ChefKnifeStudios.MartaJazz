using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record VehiclePositionUpdatedEvent(
    VehicleData Vehicle,
    PositionData Position,
    TripData? Trip
) : ISignalREvent;
