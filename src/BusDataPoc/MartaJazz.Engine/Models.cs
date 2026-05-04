namespace MartaJazz.Engine.Models;

public record BusPosition(float Latitude, float Longitude);

public record Coordinate(float Lat, float Lon);

public record BusState(float Lat, float Lon, float Speed, float Bearing);

public record EventMessage(
    string BusId,
    long Timestamp,
    BusState Current,
    Coordinate Previous,
    int IntervalMs
);
