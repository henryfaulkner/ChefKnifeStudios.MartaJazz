using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record VehicleDepartedStopEvent(
    VehicleData Vehicle,
    TripData Trip,
    string DepartedStopId,
    uint DepartedStopSequence,
    string? NextStopId,
    uint? NextStopSequence,
    int? DepartureDelaySeconds
) : ISignalREvent;
