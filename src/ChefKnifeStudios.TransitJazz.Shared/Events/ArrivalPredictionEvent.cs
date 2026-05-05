using System.Collections.Generic;
using ChefKnifeStudios.TransitJazz.Shared.EventData;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record ArrivalPredictionEvent(
    TripData? Trip,
    string? VehicleId,
    long? Timestamp,
    int? TripDelaySeconds,
    IReadOnlyList<StopTimeData>? StopTimes
) : ISignalREvent;
