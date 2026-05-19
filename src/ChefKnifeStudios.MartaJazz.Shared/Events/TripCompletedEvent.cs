using ChefKnifeStudios.MartaJazz.Shared.EventData;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record TripCompletedEvent(
    TripData Trip,
    string? VehicleId,
    string TerminalStopId,
    uint? TerminalStopSequence,
    long? ActualDepartureTime,
    int? FinalDelaySeconds
) : ISignalREvent;
