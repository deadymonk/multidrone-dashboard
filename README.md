# Multi-Vehicle Status Dashboard — Mission Planner Plugin

A  grid dashboard that monitors all connected drones simultaneously with real-time telemetry and arm/disarm controls.

## Features

- **Grid Layout** — Each drone gets a square panel with live telemetry
- **Artificial Horizon** — Visual roll/pitch indicator per drone
- **Telemetry Display** — Roll, Yaw, Altitude, Speed, GPS, HDOP, Battery, Mode
- **Click-to-Control** — Click any drone panel to arm/disarm that specific vehicle
- **Global Arm/Disarm** — Control all vehicles at once (with safety confirmations)
- **Auto-open** — Dashboard launches automatically when Mission Planner starts
- **5Hz Updates** — Smooth, real-time data refresh

## Manual Installation Guide

Mission Planner supports C# scripts dynamically compiled at runtime via its plugin system. You do **not** need Visual Studio or any separate compiler to install this plugin. 

Please follow these instructions carefully to implement the plugin into your Mission Planner installation.

### Step 1: Locate your Mission Planner Plugins Folder
By default, Mission Planner is installed in `C:\Program Files (x86)\Mission Planner\`. 
Inside this directory, there is a folder named `plugins`. 

The full path should be:
`C:\Program Files (x86)\Mission Planner\plugins\`

*Note: If you installed Mission Planner in a custom directory or are using a portable version, navigate to that specific directory and find the `plugins` folder.*

### Step 2: Download the Plugin File
Download the `MultiVehicleDashPlugin.cs` file from this repository.

### Step 3: Copy the Plugin
Move or copy the downloaded `MultiVehicleDashPlugin.cs` file directly into the `plugins` folder you located in Step 1.

You will likely need **Administrator Privileges** to copy a file into the `Program Files (x86)` directory. If Windows prompts you for permission, click **Continue**.

### Step 4: Restart Mission Planner
If Mission Planner is currently open, completely close the application. 
Launch Mission Planner again.

During the startup sequence, Mission Planner's internal compiler (Roslyn) will automatically detect the new `.cs` file in the plugins folder and compile it in the background.

### Step 5: Access the Dashboard
Once Mission Planner has fully loaded:
1. Connect your vehicles (or SITL instances) as usual.
2. The Multi-Vehicle Grid Dashboard should open **automatically** in a separate window.
3. If you accidentally close it, you can reopen it manually: Right-click anywhere on the map in the **Flight Data** screen, and click **"Multi-Vehicle Dash"** from the context menu.

## Safety Mechanisms

| Action | Safety Mechanism |
|--------|-----------------|
| Global Arm | Double-click + confirmation dialog |
| Global Disarm | Confirmation dialog |
| Individual Arm | Click panel → popup → confirm |
| Individual Disarm | Click panel → popup → immediate |

## Requirements

- Mission Planner
- .NET Framework 4.6.2+ (already required by modern Mission Planner versions)
