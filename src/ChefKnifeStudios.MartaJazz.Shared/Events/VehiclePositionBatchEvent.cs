using ChefKnifeStudios.MartaJazz.Shared.EventData;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record VehiclePositionBatchEvent(
    IEnumerable<VehiclePositionBatchEvent.VehiclePositionRecord> BatchRecords
) : ISignalREvent
{
    public sealed record VehiclePositionRecord(
        VehicleData Vehicle,
        PositionData Position,
        TripData? Trip,
        bool IsStale
    );
}