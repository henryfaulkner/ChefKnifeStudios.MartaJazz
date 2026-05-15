using System;

namespace ChefKnifeStudios.TransitJazz.Shared.Geospatial;

public readonly record struct Snap(int Index, RoutePoint Point, double DistanceKm);

public static class RouteSnapper
{
    public static Snap? FindNearest(double lat, double lon, ReadOnlySpan<RoutePoint> points)
    {
        if (points.IsEmpty) return null;

        int bestIndex = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < points.Length; i++)
        {
            double distance = HaversineCalculator.DistanceKm(lat, lon, points[i].Lat, points[i].Lon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return new Snap(bestIndex, points[bestIndex], bestDistance);
    }
}
