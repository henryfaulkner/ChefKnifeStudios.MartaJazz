using ChefKnifeStudios.TransitJazz.Shared.EventData;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

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