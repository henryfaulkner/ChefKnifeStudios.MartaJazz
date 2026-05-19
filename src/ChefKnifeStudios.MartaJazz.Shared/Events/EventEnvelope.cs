using System;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record EventEnvelope(
    string EventType,
    DateTimeOffset Timestamp,
    ISignalREvent Payload
);
