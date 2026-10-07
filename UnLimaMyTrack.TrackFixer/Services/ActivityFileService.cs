using System.Globalization;
using System.Xml.Linq;
using UnLimaMyTrack.TrackFixer.Models;

namespace UnLimaMyTrack.TrackFixer.Services;

public sealed class ActivityFileService
{
    private readonly ActivityRecalculator _recalculator = new();

    public async Task<ActivityFile> LoadAsync(string path, double movingSpeedThresholdMetersPerSecond = ActivityRecalculator.DefaultMovingSpeedThresholdMetersPerSecond)
    {
        var format = DetectFormat(path);
        var activity = format switch
        {
            ActivityFileFormat.Gpx => await LoadGpxAsync(path),
            ActivityFileFormat.Tcx => await LoadTcxAsync(path),
            ActivityFileFormat.Fit => throw new NotSupportedException("FIT loading is planned but not implemented in this first slice."),
            _ => throw new InvalidOperationException("Unknown file format.")
        };

        _recalculator.Recalculate(activity, movingSpeedThresholdMetersPerSecond);
        return activity;
    }

    public Task SaveAsync(ActivityFile activity, string path, double movingSpeedThresholdMetersPerSecond = ActivityRecalculator.DefaultMovingSpeedThresholdMetersPerSecond)
    {
        _recalculator.Recalculate(activity, movingSpeedThresholdMetersPerSecond);

        return activity.Format switch
        {
            ActivityFileFormat.Gpx => SaveGpxAsync(activity, path),
            ActivityFileFormat.Tcx => SaveTcxAsync(activity, path),
            ActivityFileFormat.Fit => throw new NotSupportedException("FIT saving is planned but not implemented in this first slice."),
            _ => throw new InvalidOperationException("Unknown file format.")
        };
    }

    private static ActivityFileFormat DetectFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".gpx" => ActivityFileFormat.Gpx,
            ".tcx" => ActivityFileFormat.Tcx,
            ".fit" => ActivityFileFormat.Fit,
            _ => throw new NotSupportedException("Supported formats are GPX, TCX, and FIT.")
        };
    }

    private static async Task<ActivityFile> LoadGpxAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        var activity = new ActivityFile { SourcePath = path, Format = ActivityFileFormat.Gpx };

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "trkpt"))
        {
            if (!TryReadDouble(element.Attribute("lat")?.Value, out var lat)
                || !TryReadDouble(element.Attribute("lon")?.Value, out var lon))
            {
                continue;
            }

            activity.Points.Add(new TrackPoint
            {
                Latitude = lat,
                Longitude = lon,
                AltitudeMeters = ReadNullableDouble(element.Elements().FirstOrDefault(e => e.Name.LocalName == "ele")?.Value),
                Timestamp = ReadNullableTime(element.Elements().FirstOrDefault(e => e.Name.LocalName == "time")?.Value)
            });
        }

        return activity;
    }

    private static async Task<ActivityFile> LoadTcxAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        var activity = new ActivityFile { SourcePath = path, Format = ActivityFileFormat.Tcx };

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Trackpoint"))
        {
            var position = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Position");
            if (position is null)
            {
                continue;
            }

            var latText = position.Elements().FirstOrDefault(e => e.Name.LocalName == "LatitudeDegrees")?.Value;
            var lonText = position.Elements().FirstOrDefault(e => e.Name.LocalName == "LongitudeDegrees")?.Value;

            if (!TryReadDouble(latText, out var lat) || !TryReadDouble(lonText, out var lon))
            {
                continue;
            }

            activity.Points.Add(new TrackPoint
            {
                Latitude = lat,
                Longitude = lon,
                Timestamp = ReadNullableTime(element.Elements().FirstOrDefault(e => e.Name.LocalName == "Time")?.Value),
                AltitudeMeters = ReadNullableDouble(element.Elements().FirstOrDefault(e => e.Name.LocalName == "AltitudeMeters")?.Value),
                DistanceMeters = ReadNullableDouble(element.Elements().FirstOrDefault(e => e.Name.LocalName == "DistanceMeters")?.Value),
                HeartRate = ReadNullableInt(element.Descendants().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value),
                Cadence = ReadNullableInt(element.Elements().FirstOrDefault(e => e.Name.LocalName == "Cadence")?.Value)
            });
        }

        return activity;
    }

    private static async Task SaveGpxAsync(ActivityFile activity, string path)
    {
        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        var document = new XDocument(
            new XElement(ns + "gpx",
                new XAttribute("version", "1.1"),
                                new XAttribute("creator", "UnLimaMyTrack Track Fixer"),
                new XElement(ns + "trk",
                    new XElement(ns + "name", Path.GetFileNameWithoutExtension(activity.SourcePath)),
                    new XElement(ns + "trkseg", activity.Points.Select(point =>
                        new XElement(ns + "trkpt",
                            new XAttribute("lat", FormatDouble(point.Latitude)),
                            new XAttribute("lon", FormatDouble(point.Longitude)),
                            point.AltitudeMeters is null ? null : new XElement(ns + "ele", FormatDouble(point.AltitudeMeters.Value)),
                            point.Timestamp is null ? null : new XElement(ns + "time", point.Timestamp.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))))))));

        await using var stream = File.Create(path);
        await document.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
    }

    private static async Task SaveTcxAsync(ActivityFile activity, string path)
    {
        XNamespace ns = "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2";
        var startedAt = activity.Points.FirstOrDefault()?.Timestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var document = new XDocument(
            new XElement(ns + "TrainingCenterDatabase",
                new XElement(ns + "Activities",
                    new XElement(ns + "Activity",
                        new XAttribute("Sport", "Biking"),
                        new XElement(ns + "Id", startedAt),
                        new XElement(ns + "Lap",
                            new XAttribute("StartTime", startedAt),
                            new XElement(ns + "TotalTimeSeconds", FormatDouble(activity.MovingTime.TotalSeconds)),
                            new XElement(ns + "DistanceMeters", FormatDouble(activity.TotalDistanceMeters)),
                            new XElement(ns + "Calories", "0"),
                            new XElement(ns + "Intensity", "Active"),
                            new XElement(ns + "TriggerMethod", "Manual"),
                            new XElement(ns + "Track", activity.Points.Select(point =>
                                new XElement(ns + "Trackpoint",
                                    point.Timestamp is null ? null : new XElement(ns + "Time", point.Timestamp.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                                    new XElement(ns + "Position",
                                        new XElement(ns + "LatitudeDegrees", FormatDouble(point.Latitude)),
                                        new XElement(ns + "LongitudeDegrees", FormatDouble(point.Longitude))),
                                    point.AltitudeMeters is null ? null : new XElement(ns + "AltitudeMeters", FormatDouble(point.AltitudeMeters.Value)),
                                    point.DistanceMeters is null ? null : new XElement(ns + "DistanceMeters", FormatDouble(point.DistanceMeters.Value)),
                                    point.HeartRate is null ? null : new XElement(ns + "HeartRateBpm", new XElement(ns + "Value", point.HeartRate.Value)),
                                    point.Cadence is null ? null : new XElement(ns + "Cadence", point.Cadence.Value)))))))));

        await using var stream = File.Create(path);
        await document.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
    }

    private static double? ReadNullableDouble(string? text) => TryReadDouble(text, out var value) ? value : null;

    private static int? ReadNullableInt(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ReadNullableTime(string? text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static bool TryReadDouble(string? text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string FormatDouble(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
