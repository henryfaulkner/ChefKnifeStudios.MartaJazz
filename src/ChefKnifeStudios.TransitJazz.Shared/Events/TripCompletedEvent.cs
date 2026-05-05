using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record TripCompletedEvent(
    TripData Trip,
    string? VehicleId,
    string TerminalStopId,
    uint? TerminalStopSequence,
    long? ActualDepartureTime,
    int? FinalDelaySeconds
) : ISignalREvent;
