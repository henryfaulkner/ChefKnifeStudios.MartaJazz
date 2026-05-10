namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;

/// <summary>
/// Encodes latitude/longitude pairs into Base32 geohash strings for spatial bucketing.
/// </summary>
public static class GeohashEncoder
{
    static readonly char[] Base32 = "0123456789bcdefghjkmnpqrstuvwxyz".ToCharArray();

    /// <summary>
    /// Encodes a coordinate pair into a geohash string of the specified precision.
    /// </summary>
    /// <param name="lat">Latitude in degrees (-90 to 90).</param>
    /// <param name="lon">Longitude in degrees (-180 to 180).</param>
    /// <param name="precision">Number of Base32 characters in the output. Each character narrows the cell by ~5 bits.</param>
    /// <returns>A geohash string of length <paramref name="precision"/>.</returns>
    public static string Encode(double lat, double lon, int precision = 5)
    {
        double minLat = -90, maxLat = 90;
        double minLon = -180, maxLon = 180;
        bool isLon = true;
        int bit = 0;
        int hashIndex = 0;
        var result = new char[precision];
        int charIndex = 0;

        while (charIndex < precision)
        {
            if (isLon)
            {
                double mid = (minLon + maxLon) / 2;
                if (lon >= mid)
                {
                    hashIndex = (hashIndex << 1) | 1;
                    minLon = mid;
                }
                else
                {
                    hashIndex <<= 1;
                    maxLon = mid;
                }
            }
            else
            {
                double mid = (minLat + maxLat) / 2;
                if (lat >= mid)
                {
                    hashIndex = (hashIndex << 1) | 1;
                    minLat = mid;
                }
                else
                {
                    hashIndex <<= 1;
                    maxLat = mid;
                }
            }

            isLon = !isLon;
            bit++;

            if (bit == 5)
            {
                result[charIndex++] = Base32[hashIndex];
                bit = 0;
                hashIndex = 0;
            }
        }

        return new string(result);
    }
}
