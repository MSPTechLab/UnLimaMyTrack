using UnLimaMyTrack.TrackFixer.Models;

namespace UnLimaMyTrack.TrackFixer.Services;

public sealed class ActivityRecalculator
{
    public const double DefaultMovingSpeedThresholdMetersPerSecond = 0.8;

    public void Recalculate(ActivityFile activity, double movingSpeedThresholdMetersPerSecond = DefaultMovingSpeedThresholdMetersPerSecond)
    {
        var totalDistance = 0d;
        var movingTime = TimeSpan.Zero;

        if (activity.Points.Count > 0)
        {
            activity.Points[0].DistanceMeters = 0;
            activity.Points[0].SpeedMetersPerSecond = null;
        }

        for (var i = 1; i < activity.Points.Count; i++)
        {
            var previous = activity.Points[i - 1];
            var current = activity.Points[i];
            var segmentDistance = GeoMath.DistanceMeters(previous, current);
            totalDistance += segmentDistance;

            var elapsed = GetPositiveElapsed(previous, current);
            double? speed = elapsed is { TotalSeconds: > 0 }
                ? segmentDistance / elapsed.Value.TotalSeconds
                : null;

            current.DistanceMeters = totalDistance;
            current.SpeedMetersPerSecond = speed;

            if (speed >= movingSpeedThresholdMetersPerSecond && elapsed is not null)
            {
                movingTime += elapsed.Value;
            }
        }

        activity.TotalDistanceMeters = totalDistance;
        activity.MovingTime = movingTime;
        activity.AverageMovingSpeedMetersPerSecond = movingTime.TotalSeconds > 0
            ? totalDistance / movingTime.TotalSeconds
            : 0;
    }

    private static TimeSpan? GetPositiveElapsed(TrackPoint previous, TrackPoint current)
    {
        if (previous.Timestamp is null || current.Timestamp is null)
        {
            return null;
        }

        var elapsed = current.Timestamp.Value - previous.Timestamp.Value;
        return elapsed > TimeSpan.Zero ? elapsed : null;
    }

    public static TrackPoint Interpolate(TrackPoint a, TrackPoint b, double lat, double lon)
    {
        var distTotal = GeoMath.DistanceMeters(a, b);
        var distA = GeoMath.DistanceMeters(a.Latitude, a.Longitude, lat, lon);
        var fraction = distTotal > 0 ? Math.Clamp(distA / distTotal, 0, 1) : 0.5;

        DateTimeOffset? interpolatedTime = null;
        if (a.Timestamp is not null && b.Timestamp is not null)
        {
            var span = b.Timestamp.Value - a.Timestamp.Value;
            interpolatedTime = a.Timestamp.Value.AddSeconds(span.TotalSeconds * fraction);
        }
        else if (a.Timestamp is not null)
        {
            interpolatedTime = a.Timestamp;
        }
        else if (b.Timestamp is not null)
        {
            interpolatedTime = b.Timestamp;
        }

        return new TrackPoint
        {
            Latitude = lat,
            Longitude = lon,
            Timestamp = interpolatedTime,
            AltitudeMeters = InterpolateDouble(a.AltitudeMeters, b.AltitudeMeters, fraction),
            HeartRate = InterpolateInt(a.HeartRate, b.HeartRate, fraction),
            Cadence = InterpolateInt(a.Cadence, b.Cadence, fraction),
            Power = InterpolateInt(a.Power, b.Power, fraction),
            TemperatureCelsius = InterpolateDouble(a.TemperatureCelsius, b.TemperatureCelsius, fraction)
        };
    }

    private static double? InterpolateDouble(double? a, double? b, double fraction)
    {
        if (a is null && b is null) return null;
        if (a is null) return b;
        if (b is null) return a;
        return a.Value + (b.Value - a.Value) * fraction;
    }

    private static int? InterpolateInt(int? a, int? b, double fraction)
    {
        if (a is null && b is null) return null;
        if (a is null) return b;
        if (b is null) return a;
        return (int)Math.Round(a.Value + (b.Value - a.Value) * fraction);
    }
}
