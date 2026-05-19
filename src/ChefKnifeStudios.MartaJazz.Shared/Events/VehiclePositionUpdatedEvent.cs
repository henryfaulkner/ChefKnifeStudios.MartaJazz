using ChefKnifeStudios.MartaJazz.Shared.EventData;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record VehiclePositionUpdatedEvent(
    VehicleData Vehicle,
    PositionData Position,
    TripData? Trip
) : ISignalREvent;
