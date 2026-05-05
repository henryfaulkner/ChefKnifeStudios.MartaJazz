using System;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record EventEnvelope(
    string EventType,
    DateTimeOffset Timestamp,
    ISignalREvent Payload
);
