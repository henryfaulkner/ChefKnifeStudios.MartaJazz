using ChefKnifeStudios.MartaJazz.Shared.EventData;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record VehicleDepartedStopEvent(
    VehicleData Vehicle,
    TripData Trip,
    string DepartedStopId,
    uint DepartedStopSequence,
    string? NextStopId,
    uint? NextStopSequence,
    int? DepartureDelaySeconds
) : ISignalREvent;
