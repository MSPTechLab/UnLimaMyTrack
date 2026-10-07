# UnLima Track Fixer

A cross-platform desktop application designed for fixing bike ride activity files containing spoofed, broken, or unrealistic GPS track points.
There are no magic algoritms, program just detect waypoints outside of some trusted radius and deletes it from track. Later it is possible to edit segments to return to more or less real track.

## Purpose & Functions

- **Multi-Format Activity Support**: Load and save activity files in **GPX**, **TCX**.
- **OpenStreetMap Map Viewer**: Interactive map display powered by Mapsui.
- **Trusted Anchor & Radius**: Define a trusted geographic area using an anchor point and trusted radius (default 100 km) to automatically remove erroneous points outside the area.
- **Unrealistic Speed & Jump Removal**: Detects and cleans up impossible location jumps and consecutive GPS anomalies.
- **Manual Track Editing**:
  - Drag existing track points to new locations.
  - Insert new track points via segment midpoint handles.
  - Real-time route segment redrawing during dragging.
- **Automatic Telemetry Interpolation**: Automatically interpolates timestamps and sensor data (altitude, heart rate, cadence, power, temperature) for inserted points while preserving original timestamps on existing points.
- **Metric Recalculation**: Automatically recalculates point-to-point distances, total distance, moving time, average speed, and moving average speed.
- **Point Inspection**: Right-click any track point in editing mode to inspect its complete telemetry data.
## TBD
-  Proper FIT file support.
## Note
*This application was written by AI with limited testing. So, it is not guaranted that all data is properly reculculated. 
Note recommented to use if your track is necessary for prove of your actual ride.*
