namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>
/// A single coordinate on a transit route, stored as a value type in the spatial index.
/// </summary>
/// <param name="RouteId">The GTFS route identifier this point belongs to.</param>
/// <param name="Lat">Latitude in degrees.</param>
/// <param name="Lon">Longitude in degrees.</param>
public readonly record struct RoutePoint(string RouteId, double Lat, double Lon);
