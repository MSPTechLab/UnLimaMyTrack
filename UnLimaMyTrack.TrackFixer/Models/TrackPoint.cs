namespace UnLimaMyTrack.TrackFixer.Models;

public sealed class TrackPoint
{
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public double? AltitudeMeters { get; set; }
    public double? DistanceMeters { get; set; }
    public double? SpeedMetersPerSecond { get; set; }
    public int? HeartRate { get; set; }
    public int? Cadence { get; set; }
    public int? Power { get; set; }
    public double? TemperatureCelsius { get; set; }
}
