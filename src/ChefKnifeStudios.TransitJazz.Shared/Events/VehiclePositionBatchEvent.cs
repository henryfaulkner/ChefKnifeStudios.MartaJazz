using ChefKnifeStudios.TransitJazz.Shared.EventData;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record VehiclePositionBatchEvent(
    IEnumerable<(VehicleData, PositionData, TripData?)> BatchRecords
) : ISignalREvent;