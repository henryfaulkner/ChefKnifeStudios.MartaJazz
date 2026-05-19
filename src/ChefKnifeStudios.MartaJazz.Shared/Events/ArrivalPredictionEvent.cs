using System.Collections.Generic;
using ChefKnifeStudios.MartaJazz.Shared.EventData;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record ArrivalPredictionEvent(
    TripData? Trip,
    string? VehicleId,
    long? Timestamp,
    int? TripDelaySeconds,
    IReadOnlyList<StopTimeData>? StopTimes
) : ISignalREvent;
