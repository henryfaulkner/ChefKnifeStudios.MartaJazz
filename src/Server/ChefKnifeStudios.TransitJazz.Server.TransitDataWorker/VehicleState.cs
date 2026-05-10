namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>
/// Tracks a vehicle's most recent nearest route point for delta detection.
/// Stored in a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> keyed by vehicle ID.
/// </summary>
/// <param name="NearestLat">Latitude of the nearest route point in degrees.</param>
/// <param name="NearestLon">Longitude of the nearest route point in degrees.</param>
/// <param name="LastUpdated">UTC timestamp of the last state update, used for out-of-order guards and stale pruning.</param>
/// <param name="RouteId">The GTFS route identifier the vehicle is currently snapped to.</param>
/// <param name="SpeedMetersPerSec">Vehicle speed from the GTFS-RT feed, if available.</param>
/// <param name="Bearing">Vehicle bearing in degrees from the GTFS-RT feed, if available.</param>
public record VehicleState(
    double NearestLat,
    double NearestLon,
    DateTime LastUpdated,
    string RouteId,
    float? SpeedMetersPerSec,
    float? Bearing
);
