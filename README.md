# Multi-Vehicle Status Dashboard — Mission Planner Plugin

A CCTV-style grid dashboard that monitors all connected drones simultaneously with real-time telemetry and arm/disarm controls.

## Features

- **CCTV Grid Layout** — Each drone gets a square panel with live telemetry
- **Artificial Horizon** — Visual roll/pitch indicator per drone
- **Telemetry Display** — Roll, Yaw, Altitude, Speed, GPS, HDOP, Battery, Mode
- **Click-to-Control** — Click any drone panel to arm/disarm that specific vehicle
- **Global Arm/Disarm** — Control all vehicles at once (with safety confirmations)
- **Auto-open** — Dashboard launches automatically when Mission Planner starts
- **5Hz Updates** — Smooth, real-time data refresh

## Installation

1. Copy `MultiVehicleDashPlugin.cs` to your Mission Planner plugins folder:
   ```
   C:\Program Files (x86)\Mission Planner\plugins\
   ```

2. Restart Mission Planner — the plugin compiles automatically at startup.

3. The dashboard opens automatically. You can also access it by right-clicking the Flight Data map → **"Multi-Vehicle Dash"**.

## Safety

| Action | Safety Mechanism |
|--------|-----------------|
| Global Arm | Double-click + confirmation dialog |
| Global Disarm | Confirmation dialog |
| Individual Arm | Click panel → popup → confirm |
| Individual Disarm | Click panel → popup → immediate |

## Requirements

- Mission Planner (installed at `C:\Program Files (x86)\Mission Planner\`)
- .NET Framework 4.6.2+
