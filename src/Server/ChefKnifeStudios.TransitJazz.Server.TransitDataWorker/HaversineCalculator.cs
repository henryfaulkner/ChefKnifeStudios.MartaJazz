using System;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>
/// Computes great-circle distances between coordinate pairs using the Haversine formula.
/// </summary>
public static class HaversineCalculator
{
    const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Calculates the great-circle distance in kilometers between two points on Earth.
    /// </summary>
    /// <param name="lat1">Latitude of the first point in degrees.</param>
    /// <param name="lon1">Longitude of the first point in degrees.</param>
    /// <param name="lat2">Latitude of the second point in degrees.</param>
    /// <param name="lon2">Longitude of the second point in degrees.</param>
    /// <returns>Distance in kilometers.</returns>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = DegreesToRadians(lat2 - lat1);
        double dLon = DegreesToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
