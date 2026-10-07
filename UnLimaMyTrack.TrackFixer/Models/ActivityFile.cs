namespace UnLimaMyTrack.TrackFixer.Models;

public sealed class ActivityFile
{
    public required string SourcePath { get; init; }
    public required ActivityFileFormat Format { get; init; }
    public List<TrackPoint> Points { get; } = [];
    public double TotalDistanceMeters { get; set; }
    public TimeSpan MovingTime { get; set; }
    public double AverageMovingSpeedMetersPerSecond { get; set; }
}
