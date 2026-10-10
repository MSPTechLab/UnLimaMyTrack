using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;
using UnLimaMyTrack.TrackFixer.Models;
using UnLimaMyTrack.TrackFixer.Services;

namespace UnLimaMyTrack.TrackFixer.Views;

public partial class MainWindow : Window
{
    private readonly ActivityFileService _fileService = new();
    private readonly ActivityRecalculator _recalculator = new();
    private readonly TrackCleaner _cleaner = new();
    private readonly MemoryLayer _routeLayer = new("Route");
    private readonly MemoryLayer _pointsLayer = new("Points");
    private readonly MemoryLayer _midpointsLayer = new("Midpoints");
    private readonly MemoryLayer _anchorLayer = new("Anchor");
    private ActivityFile? _activity;
    private double? _anchorLatitude;
    private double? _anchorLongitude;
    private bool _isSettingAnchor;
    private bool _isSettingRouteStart;
    private bool _isSettingRouteEnd;
    private bool _isEditingEnabled;
    private int? _draggedPointIndex;
    private int? _draggedMidpointIndex;
    private MPoint? _currentDragWorld;
    private readonly Stack<ActivityState> _undoStack = new();

    public MainWindow()
    {
        InitializeComponent();
        InitializeMap();
    }

    private void InitializeMap()
    {
        ActivityMap.Map = new Map();
        ActivityMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());
        ActivityMap.Map.Layers.Add(_routeLayer);
        ActivityMap.Map.Layers.Add(_pointsLayer);
        ActivityMap.Map.Layers.Add(_midpointsLayer);
        ActivityMap.Map.Layers.Add(_anchorLayer);
        ActivityMap.PointerPressed += ActivityMapPointerPressed;
        ActivityMap.PointerMoved += ActivityMapPointerMoved;
        ActivityMap.PointerReleased += ActivityMapPointerReleased;
        ActivityMap.Map.Navigator.ViewportChanged += (s, e) =>
        {
            if (_isEditingEnabled)
            {
                RefreshMap(false);
            }
        };
    }

    private async void OpenActivityClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Open activity file",
            FileTypeFilter =
            [
                new FilePickerFileType("Activity files") { Patterns = ["*.gpx", "*.tcx", "*.fit"] }
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        try
        {
            var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);
            _activity = await _fileService.LoadAsync(path, movingSpeed);
            var first = _activity.Points.FirstOrDefault();
            _anchorLatitude = first?.Latitude;
            _anchorLongitude = first?.Longitude;
            _isSettingAnchor = false;
            _isSettingRouteStart = false;
            _isSettingRouteEnd = false;
            SetAnchorButton.Content = "Set anchor";
            SetRouteStartButton.Content = "Set route start";
            SetRouteEndButton.Content = "Set route end";
            _undoStack.Clear();
            UndoButton.IsEnabled = false;
            RefreshMap(true);
            UpdateLabels($"Loaded {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message);
        }
    }

    private void RemoveBrokenPointsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activity is null || _anchorLatitude is null || _anchorLongitude is null)
        {
            UpdateStatus("Load an activity before removing points.");
            return;
        }

        var radiusMeters = Convert.ToDouble(RadiusInput.Value ?? 100m) * 1000;
        var unrealisticSpeed = Convert.ToDouble(UnrealisticSpeedInput.Value ?? 25m);
        var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);
        PushUndoState();
        var removed = _cleaner.RemoveBrokenPoints(_activity, _anchorLatitude.Value, _anchorLongitude.Value, radiusMeters, unrealisticSpeed);
        _recalculator.Recalculate(_activity, movingSpeed);
        RefreshMap(false);
        UpdateLabels($"Removed {removed} broken point(s).");
    }

    private void SetAnchorClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activity is null)
        {
            UpdateStatus("Load an activity before setting anchor.");
            return;
        }

        _isSettingRouteStart = false;
        _isSettingRouteEnd = false;
        _isSettingAnchor = true;
        SetAnchorButton.Content = "Click map...";
        SetRouteStartButton.Content = "Set route start";
        SetRouteEndButton.Content = "Set route end";
        UpdateStatus("Click once on the map to set the anchor point.");
    }

    private void SetRouteStartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activity is null || _activity.Points.Count == 0)
        {
            UpdateStatus("Load an activity before setting route start.");
            return;
        }

        _isSettingAnchor = false;
        _isSettingRouteEnd = false;
        _isSettingRouteStart = true;
        SetRouteStartButton.Content = "Click map...";
        SetRouteEndButton.Content = "Set route end";
        SetAnchorButton.Content = "Set anchor";
        UpdateStatus("Click once on the map to set the route start point.");
    }

    private void SetRouteEndClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activity is null || _activity.Points.Count == 0)
        {
            UpdateStatus("Load an activity before setting route end.");
            return;
        }

        _isSettingAnchor = false;
        _isSettingRouteStart = false;
        _isSettingRouteEnd = true;
        SetRouteEndButton.Content = "Click map...";
        SetRouteStartButton.Content = "Set route start";
        SetAnchorButton.Content = "Set anchor";
        UpdateStatus("Click once on the map to set the route end point.");
    }

    private void ToggleEditClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isEditingEnabled = !_isEditingEnabled;
        ToggleEditButton.Content = _isEditingEnabled ? "Editing mode: ON" : "Editing mode: OFF";
        if (!_isEditingEnabled)
        {
            _draggedPointIndex = null;
            _draggedMidpointIndex = null;
            _currentDragWorld = null;
            if (ActivityMap.Map?.Navigator is not null)
            {
                ActivityMap.Map.Navigator.PanLock = false;
            }
        }
        RefreshMap(false);
        UpdateStatus(_isEditingEnabled ? "Editing mode enabled. Click and drag points or green midpoints to edit." : "Editing mode disabled.");
    }

    private async void SaveActivityClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activity is null)
        {
            UpdateStatus("Load an activity before saving.");
            return;
        }

        var extension = _activity.Format.ToString().ToLowerInvariant();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save corrected activity",
            SuggestedFileName = Path.GetFileNameWithoutExtension(_activity.SourcePath) + "-corrected." + extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }]
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        try
        {
            var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);
            await _fileService.SaveAsync(_activity, path, movingSpeed);
            UpdateStatus($"Saved corrected activity to {path}.");
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message);
        }
    }

    private void UpdateLabels(string status)
    {
        UpdateStatus(status);
        if (_activity is null)
        {
            return;
        }

        AnchorText.Text = _anchorLatitude is null || _anchorLongitude is null
            ? "No anchor point."
            : $"{_anchorLatitude:0.000000}, {_anchorLongitude:0.000000}";

        StatsText.Text = $"Points: {_activity.Points.Count}\nDistance: {_activity.TotalDistanceMeters / 1000:0.00} km\nMoving time: {_activity.MovingTime:hh\\:mm\\:ss}\nAvg moving speed: {_activity.AverageMovingSpeedMetersPerSecond * 3.6:0.0} km/h";
    }

    private void UpdateStatus(string status)
    {
        StatusText.Text = status;
    }

    private void ShowPointDetails(TrackPoint p, int index)
    {
        var details = $"Point #{index + 1}\n" +
                      $"Latitude: {p.Latitude:0.########}\n" +
                      $"Longitude: {p.Longitude:0.########}\n" +
                      $"Timestamp: {(p.Timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A")}\n" +
                      $"Altitude: {(p.AltitudeMeters is not null ? $"{p.AltitudeMeters:0.1} m" : "N/A")}\n" +
                      $"Distance: {(p.DistanceMeters is not null ? $"{p.DistanceMeters / 1000:0.02} km" : "N/A")}\n" +
                      $"Speed: {(p.SpeedMetersPerSecond is not null ? $"{p.SpeedMetersPerSecond * 3.6:0.1} km/h" : "N/A")}\n" +
                      $"Heart Rate: {(p.HeartRate is not null ? $"{p.HeartRate} bpm" : "N/A")}\n" +
                      $"Cadence: {(p.Cadence is not null ? $"{p.Cadence} rpm" : "N/A")}\n" +
                      $"Power: {(p.Power is not null ? $"{p.Power} W" : "N/A")}\n" +
                      $"Temperature: {(p.TemperatureCelsius is not null ? $"{p.TemperatureCelsius:0.1} °C" : "N/A")}";

        var dialog = new Window
        {
            Title = $"Track Point #{index + 1}",
            Width = 320,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = details,
                    Margin = new Avalonia.Thickness(16),
                    FontSize = 14,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };

        dialog.ShowDialog(this);
    }

    private void ActivityMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activity is null || ActivityMap.Map?.Navigator.Viewport is null)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(ActivityMap);
        var position = e.GetPosition(ActivityMap);
        var world = ActivityMap.Map.Navigator.Viewport.ScreenToWorld(position.X, position.Y);
        var lonLat = ToLonLat(world.X, world.Y);

        if (pointerPoint.Properties.IsRightButtonPressed)
        {
            if (!_isEditingEnabled)
            {
                return;
            }

            var resolution = ActivityMap.Map?.Navigator.Viewport.Resolution ?? 1.0;
            double maxDistMeters = resolution * 15.0;
            int? bestPointIndex = null;
            double rightBestPointDist = double.MaxValue;

            for (var i = 0; i < _activity.Points.Count; i++)
            {
                var p = _activity.Points[i];
                var dist = GeoMath.DistanceMeters(p.Latitude, p.Longitude, lonLat.Latitude, lonLat.Longitude);
                if (dist <= maxDistMeters && dist < rightBestPointDist)
                {
                    rightBestPointDist = dist;
                    bestPointIndex = i;
                }
            }

            if (bestPointIndex is not null)
            {
                ShowPointDetails(_activity.Points[bestPointIndex.Value], bestPointIndex.Value);
                e.Handled = true;
                return;
            }
            return;
        }

        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_isSettingAnchor)
        {
            PushUndoState();
            _anchorLatitude = lonLat.Latitude;
            _anchorLongitude = lonLat.Longitude;
            _isSettingAnchor = false;
            SetAnchorButton.Content = "Set anchor";
            RefreshMap(false);
            UpdateLabels("Anchor point updated.");
            e.Handled = true;
            return;
        }

        if (_isSettingRouteStart)
        {
            if (_activity.Points.Count > 0)
            {
                PushUndoState();
                _activity.Points[0].Latitude = lonLat.Latitude;
                _activity.Points[0].Longitude = lonLat.Longitude;
                var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);
                _recalculator.Recalculate(_activity, movingSpeed);
            }
            _isSettingRouteStart = false;
            SetRouteStartButton.Content = "Set route start";
            RefreshMap(false);
            UpdateLabels("Route start point updated.");
            e.Handled = true;
            return;
        }

        if (_isSettingRouteEnd)
        {
            if (_activity.Points.Count > 0)
            {
                PushUndoState();
                var last = _activity.Points[^1];
                last.Latitude = lonLat.Latitude;
                last.Longitude = lonLat.Longitude;
                var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);
                _recalculator.Recalculate(_activity, movingSpeed);
            }
            _isSettingRouteEnd = false;
            SetRouteEndButton.Content = "Set route end";
            RefreshMap(false);
            UpdateLabels("Route end point updated.");
            e.Handled = true;
            return;
        }

        if (!_isEditingEnabled)
        {
            return;
        }

        var res = ActivityMap.Map?.Navigator.Viewport.Resolution ?? 1.0;
        double maxDist = res * 15.0;

        int? bestPoint = null;
        double bestPointDist = double.MaxValue;

        for (var i = 0; i < _activity.Points.Count; i++)
        {
            var p = _activity.Points[i];
            var dist = GeoMath.DistanceMeters(p.Latitude, p.Longitude, lonLat.Latitude, lonLat.Longitude);
            if (dist <= maxDist && dist < bestPointDist)
            {
                bestPointDist = dist;
                bestPoint = i;
            }
        }

        int? bestMidpoint = null;
        double bestMidpointDist = double.MaxValue;

        for (var i = 0; i < _activity.Points.Count - 1; i++)
        {
            var a = _activity.Points[i];
            var b = _activity.Points[i + 1];
            var midLat = (a.Latitude + b.Latitude) / 2.0;
            var midLon = (a.Longitude + b.Longitude) / 2.0;
            var dist = GeoMath.DistanceMeters(midLat, midLon, lonLat.Latitude, lonLat.Longitude);
            if (dist <= maxDist && dist < bestMidpointDist)
            {
                bestMidpointDist = dist;
                bestMidpoint = i;
            }
        }

        if (bestPoint is not null && (bestMidpoint is null || bestPointDist <= bestMidpointDist))
        {
            _draggedPointIndex = bestPoint.Value;
            _currentDragWorld = world;
            if (ActivityMap.Map?.Navigator is not null)
            {
                ActivityMap.Map.Navigator.PanLock = true;
            }
            e.Pointer.Capture(ActivityMap);
            e.Handled = true;
            return;
        }
        else if (bestMidpoint is not null)
        {
            _draggedMidpointIndex = bestMidpoint.Value;
            _currentDragWorld = world;
            if (ActivityMap.Map?.Navigator is not null)
            {
                ActivityMap.Map.Navigator.PanLock = true;
            }
            e.Pointer.Capture(ActivityMap);
            e.Handled = true;
            return;
        }
    }

    private void ActivityMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if ((_draggedPointIndex is null && _draggedMidpointIndex is null) || ActivityMap.Map?.Navigator.Viewport is null)
        {
            return;
        }

        var position = e.GetPosition(ActivityMap);
        _currentDragWorld = ActivityMap.Map.Navigator.Viewport.ScreenToWorld(position.X, position.Y);
        RefreshMap(false);
        e.Handled = true;
    }

    private void ActivityMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        if (ActivityMap.Map?.Navigator is not null)
        {
            ActivityMap.Map.Navigator.PanLock = false;
        }

        if (_activity is null || (_draggedPointIndex is null && _draggedMidpointIndex is null) || _currentDragWorld is null)
        {
            _draggedPointIndex = null;
            _draggedMidpointIndex = null;
            _currentDragWorld = null;
            return;
        }

        var lonLat = ToLonLat(_currentDragWorld.X, _currentDragWorld.Y);
        var movingSpeed = Convert.ToDouble(MovingSpeedInput.Value ?? 0.8m);

        if (_draggedPointIndex is not null)
        {
            var i = _draggedPointIndex.Value;
            if (i >= 0 && i < _activity.Points.Count)
            {
                PushUndoState();
                _activity.Points[i].Latitude = lonLat.Latitude;
                _activity.Points[i].Longitude = lonLat.Longitude;
            }
            _draggedPointIndex = null;
        }
        else if (_draggedMidpointIndex is not null)
        {
            var i = _draggedMidpointIndex.Value;
            if (i >= 0 && i < _activity.Points.Count - 1)
            {
                PushUndoState();
                var a = _activity.Points[i];
                var b = _activity.Points[i + 1];
                var newNode = ActivityRecalculator.Interpolate(a, b, lonLat.Latitude, lonLat.Longitude);
                _activity.Points.Insert(i + 1, newNode);
            }
            _draggedMidpointIndex = null;
        }

        _currentDragWorld = null;
        _recalculator.Recalculate(_activity, movingSpeed);
        RefreshMap(false);
        UpdateLabels("Track updated.");
        e.Handled = true;
    }

    private void RefreshMap(bool zoomToRoute)
    {
        if (ActivityMap.Map is null)
        {
            return;
        }

        _routeLayer.Features = CreateRouteFeatures();
        _pointsLayer.Features = CreatePointFeatures();
        _midpointsLayer.Features = CreateMidpointFeatures();
        _anchorLayer.Features = CreateAnchorFeatures();
        ActivityMap.Map.RefreshData();
        ActivityMap.ForceUpdate();

        if (zoomToRoute && _activity?.Points.Count > 0)
        {
            var bounds = CreateRouteBounds(_activity.Points);
            if (bounds is not null)
            {
                ActivityMap.Map.Navigator.ZoomToBox(bounds, MBoxFit.Fit, 0);
            }
        }
    }

    private IEnumerable<IFeature> CreatePointFeatures()
    {
        if (_activity is null || !_isEditingEnabled || _activity.Points.Count == 0)
        {
            return [];
        }

        var resolution = ActivityMap.Map?.Navigator.Viewport.Resolution ?? 1.0;
        const double minPixelSpacing = 30.0;
        double minMetersSpacing = minPixelSpacing * resolution;
        double halfSize = (10.0 * resolution) / 2.0;

        var features = new List<IFeature>();
        MPoint? lastRenderedWeb = null;

        for (var i = 0; i < _activity.Points.Count; i++)
        {
            var p = _activity.Points[i];
            var lat = p.Latitude;
            var lon = p.Longitude;

            var isDragged = (_draggedPointIndex == i);
            if (isDragged && _currentDragWorld is not null)
            {
                var ll = ToLonLat(_currentDragWorld.X, _currentDragWorld.Y);
                lat = ll.Latitude;
                lon = ll.Longitude;
            }

            var web = ToWebMercator(lat, lon);
            bool forceRender = (i == 0 || i == _activity.Points.Count - 1 || isDragged);

            if (!forceRender && lastRenderedWeb is not null)
            {
                double distMeters = Math.Sqrt(Math.Pow(web.X - lastRenderedWeb.X, 2) + Math.Pow(web.Y - lastRenderedWeb.Y, 2));
                if (distMeters < minMetersSpacing)
                {
                    continue;
                }
            }

            var coords = new Coordinate[]
            {
                new(web.X - halfSize, web.Y + halfSize),
                new(web.X + halfSize, web.Y + halfSize),
                new(web.X + halfSize, web.Y - halfSize),
                new(web.X - halfSize, web.Y - halfSize),
                new(web.X - halfSize, web.Y + halfSize)
            };

            var feature = new GeometryFeature
            {
                Geometry = new Polygon(new LinearRing(coords))
            };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(Color.FromString("#2563EB")),
                Line = new Pen(Color.White, 1.5)
            });
            features.Add(feature);
            lastRenderedWeb = web;
        }
        return features;
    }

    private IEnumerable<IFeature> CreateMidpointFeatures()
    {
        if (_activity is null || !_isEditingEnabled || _activity.Points.Count < 2)
        {
            return [];
        }

        var resolution = ActivityMap.Map?.Navigator.Viewport.Resolution ?? 1.0;
        const double minPixelSpacing = 40.0;
        double minMetersSpacing = minPixelSpacing * resolution;
        double halfSize = (8.0 * resolution) / 2.0;

        var features = new List<IFeature>();
        MPoint? lastRenderedWeb = null;

        for (var i = 0; i < _activity.Points.Count - 1; i++)
        {
            var a = _activity.Points[i];
            var b = _activity.Points[i + 1];
            var lat = (a.Latitude + b.Latitude) / 2.0;
            var lon = (a.Longitude + b.Longitude) / 2.0;

            var isDragged = (_draggedMidpointIndex == i);
            if (isDragged && _currentDragWorld is not null)
            {
                var ll = ToLonLat(_currentDragWorld.X, _currentDragWorld.Y);
                lat = ll.Latitude;
                lon = ll.Longitude;
            }

            var web = ToWebMercator(lat, lon);

            if (!isDragged && lastRenderedWeb is not null)
            {
                double distMeters = Math.Sqrt(Math.Pow(web.X - lastRenderedWeb.X, 2) + Math.Pow(web.Y - lastRenderedWeb.Y, 2));
                if (distMeters < minMetersSpacing)
                {
                    continue;
                }
            }

            var coords = new Coordinate[]
            {
                new(web.X - halfSize, web.Y + halfSize),
                new(web.X + halfSize, web.Y + halfSize),
                new(web.X + halfSize, web.Y - halfSize),
                new(web.X - halfSize, web.Y - halfSize),
                new(web.X - halfSize, web.Y + halfSize)
            };

            var feature = new GeometryFeature
            {
                Geometry = new Polygon(new LinearRing(coords))
            };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(Color.FromString("#10B981")),
                Line = new Pen(Color.White, 1.5)
            });
            features.Add(feature);
            lastRenderedWeb = web;
        }
        return features;
    }

    private IEnumerable<IFeature> CreateRouteFeatures()
    {
        if (_activity is null || _activity.Points.Count < 2)
        {
            return [];
        }

        var coordinates = new List<Coordinate>();
        for (var i = 0; i < _activity.Points.Count; i++)
        {
            var p = _activity.Points[i];
            var lat = p.Latitude;
            var lon = p.Longitude;

            if (_draggedPointIndex == i && _currentDragWorld is not null)
            {
                var ll = ToLonLat(_currentDragWorld.X, _currentDragWorld.Y);
                lat = ll.Latitude;
                lon = ll.Longitude;
            }

            var web = ToWebMercator(lat, lon);
            coordinates.Add(new Coordinate(web.X, web.Y));

            if (_draggedMidpointIndex == i && _currentDragWorld is not null)
            {
                var ll = ToLonLat(_currentDragWorld.X, _currentDragWorld.Y);
                var midWeb = ToWebMercator(ll.Latitude, ll.Longitude);
                coordinates.Add(new Coordinate(midWeb.X, midWeb.Y));
            }
        }

        var feature = new GeometryFeature
        {
            Geometry = new LineString([.. coordinates])
        };
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromString("#2563EB"), 4)
        });

        return [feature];
    }

    private IEnumerable<IFeature> CreateAnchorFeatures()
    {
        if (_anchorLatitude is null || _anchorLongitude is null)
        {
            return [];
        }

        var point = ToWebMercator(_anchorLatitude.Value, _anchorLongitude.Value);
        var feature = new PointFeature(new MPoint(point.X, point.Y));
        feature.Styles.Add(new SymbolStyle
        {
            Fill = new Brush(Color.FromString("#EF4444")),
            Outline = new Pen(Color.White, 3),
            SymbolScale = 0.9
        });

        return [feature];
    }

    private static MRect? CreateRouteBounds(IReadOnlyList<TrackPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var projected = points.Select(point => ToWebMercator(point.Latitude, point.Longitude)).ToList();
        return new MRect(projected.Min(p => p.X), projected.Min(p => p.Y), projected.Max(p => p.X), projected.Max(p => p.Y));
    }

    private static MPoint ToWebMercator(double latitude, double longitude)
    {
        const double originShift = 20037508.342789244;
        var x = longitude * originShift / 180.0;
        var y = Math.Log(Math.Tan((90.0 + latitude) * Math.PI / 360.0)) / (Math.PI / 180.0);
        y = y * originShift / 180.0;
        return new MPoint(x, y);
    }

    private static (double Latitude, double Longitude) ToLonLat(double x, double y)
    {
        const double originShift = 20037508.342789244;
        var longitude = x / originShift * 180.0;
        var latitude = y / originShift * 180.0;
        latitude = 180.0 / Math.PI * (2.0 * Math.Atan(Math.Exp(latitude * Math.PI / 180.0)) - Math.PI / 2.0);
        return (latitude, longitude);
    }

    private void UndoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        PerformUndo();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            PerformUndo();
            e.Handled = true;
        }
    }

    private void PerformUndo()
    {
        if (_activity is null || _undoStack.Count == 0)
        {
            return;
        }

        var state = _undoStack.Pop();
        RestoreState(state);
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RefreshMap(false);
        UpdateLabels("Undo performed.");
    }

    private void PushUndoState()
    {
        if (_activity is null)
        {
            return;
        }

        var clonedPoints = _activity.Points.Select(p => new TrackPoint
        {
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Timestamp = p.Timestamp,
            AltitudeMeters = p.AltitudeMeters,
            DistanceMeters = p.DistanceMeters,
            SpeedMetersPerSecond = p.SpeedMetersPerSecond,
            HeartRate = p.HeartRate,
            Cadence = p.Cadence,
            Power = p.Power,
            TemperatureCelsius = p.TemperatureCelsius
        }).ToList();

        var state = new ActivityState(
            clonedPoints,
            _activity.TotalDistanceMeters,
            _activity.MovingTime,
            _activity.AverageMovingSpeedMetersPerSecond,
            _anchorLatitude,
            _anchorLongitude
        );

        _undoStack.Push(state);
        UndoButton.IsEnabled = true;
    }

    private void RestoreState(ActivityState state)
    {
        if (_activity is null)
        {
            return;
        }

        _activity.Points.Clear();
        _activity.Points.AddRange(state.Points.Select(p => new TrackPoint
        {
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Timestamp = p.Timestamp,
            AltitudeMeters = p.AltitudeMeters,
            DistanceMeters = p.DistanceMeters,
            SpeedMetersPerSecond = p.SpeedMetersPerSecond,
            HeartRate = p.HeartRate,
            Cadence = p.Cadence,
            Power = p.Power,
            TemperatureCelsius = p.TemperatureCelsius
        }));
        _activity.TotalDistanceMeters = state.TotalDistanceMeters;
        _activity.MovingTime = state.MovingTime;
        _activity.AverageMovingSpeedMetersPerSecond = state.AverageMovingSpeedMetersPerSecond;
        _anchorLatitude = state.AnchorLatitude;
        _anchorLongitude = state.AnchorLongitude;
    }

    private sealed record ActivityState(
        List<TrackPoint> Points,
        double TotalDistanceMeters,
        TimeSpan MovingTime,
        double AverageMovingSpeedMetersPerSecond,
        double? AnchorLatitude,
        double? AnchorLongitude
    );
}
