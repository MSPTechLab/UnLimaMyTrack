using UnLimaMyTrack.TrackFixer.Models;

namespace UnLimaMyTrack.TrackFixer.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6371008.8;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var rLat1 = ToRadians(lat1);
        var rLat2 = ToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(rLat1) * Math.Cos(rLat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    public static double DistanceMeters(TrackPoint a, TrackPoint b) =>
        DistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
