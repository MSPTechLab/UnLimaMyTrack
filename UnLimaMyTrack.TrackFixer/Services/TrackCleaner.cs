using UnLimaMyTrack.TrackFixer.Models;

namespace UnLimaMyTrack.TrackFixer.Services;

public sealed class TrackCleaner
{
    public const double DefaultUnrealisticSpeedMetersPerSecond = 25.0;

    public int RemoveBrokenPoints(ActivityFile activity, double anchorLatitude, double anchorLongitude, double radiusMeters, double unrealisticSpeedMetersPerSecond = DefaultUnrealisticSpeedMetersPerSecond)
    {
        var originalCount = activity.Points.Count;
        var filtered = activity.Points
            .Where(point => GeoMath.DistanceMeters(anchorLatitude, anchorLongitude, point.Latitude, point.Longitude) <= radiusMeters)
            .ToList();

        var cleaned = RemoveUnrealisticRuns(filtered, unrealisticSpeedMetersPerSecond);
        activity.Points.Clear();
        activity.Points.AddRange(cleaned);

        return originalCount - activity.Points.Count;
    }

    private static List<TrackPoint> RemoveUnrealisticRuns(IReadOnlyList<TrackPoint> points, double unrealisticSpeedMetersPerSecond)
    {
        if (points.Count < 3)
        {
            return points.ToList();
        }

        var keep = new List<TrackPoint> { points[0] };

        for (var i = 1; i < points.Count - 1; i++)
        {
            var previousKept = keep[^1];
            var current = points[i];
            var next = points[i + 1];

            if (IsUnrealistic(previousKept, current, unrealisticSpeedMetersPerSecond) && !IsUnrealistic(previousKept, next, unrealisticSpeedMetersPerSecond))
            {
                continue;
            }

            keep.Add(current);
        }

        keep.Add(points[^1]);
        return keep;
    }

    private static bool IsUnrealistic(TrackPoint a, TrackPoint b, double unrealisticSpeedMetersPerSecond)
    {
        if (a.Timestamp is null || b.Timestamp is null)
        {
            return false;
        }

        var elapsed = b.Timestamp.Value - a.Timestamp.Value;
        if (elapsed.TotalSeconds <= 0)
        {
            return false;
        }

        return GeoMath.DistanceMeters(a, b) / elapsed.TotalSeconds > unrealisticSpeedMetersPerSecond;
    }
}
