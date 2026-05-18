# Multi-Vehicle Swarm Dashboard Plugin for Mission Planner

This repository contains a fully functional Multi-Vehicle Dashboard Plugin for Mission Planner, designed to monitor and control up to 10 vehicles (including SITL instances) simultaneously from a unified, modern interface.

## Option 1: Quick Installation (Using the Pre-Compiled `.dll`)

If you just want to use the dashboard without compiling the code, follow these steps:

1. **Download the `.dll`**: Grab the `MultiVehicleDashPlugin.dll` file from this repository.
2. **Locate your Mission Planner Plugins Folder**:
   Navigate to `C:\Program Files (x86)\Mission Planner\plugins\` (or wherever you installed Mission Planner).
3. **Paste the `.dll`**: Place the `MultiVehicleDashPlugin.dll` file directly into the `plugins` folder.
   *(Reference Image: Picture of pasting a DLL file into the Windows File Explorer at the plugins path)*
4. **Restart Mission Planner**: Open Mission Planner. You should now see a new tab/menu option for the "Multi-Vehicle Dash". 
5. **Connecting Drones**: Connect your drones (or SITL instances) as usual via UDP/TCP or Telemetry Radios. The dashboard will automatically detect them and spawn live data panels. Disconnected drones will automatically be flagged as `DISCONNECTED` based on real-time heartbeat tracking.

## Option 2: Edit and Compile from Source Code

If you want to modify the dashboard UI, add features, or compile the source code yourself, follow these steps:

### Prerequisites:
- Visual Studio (or .NET SDK for command line)
- Mission Planner installed on your system (the `.csproj` references Mission Planner's built-in `.dll` files to compile).

### Steps:
1. **Clone the Repository**:
   Download `MultiVehicleDashPlugin.cs` and `MultiVehicleDashPlugin.csproj`.
2. **Verify Assembly References**:
   Open the `.csproj` file and ensure the `<HintPath>` paths match your local Mission Planner installation (default is `C:\Program Files (x86)\Mission Planner\`).
3. **Edit the Code**:
   Modify the UI or MAVLink logic inside `MultiVehicleDashPlugin.cs`. 
   *Note: The plugin currently tracks dynamic disconnection states efficiently using `cs.connected` to support synchronized timestamp drops for SITL simulation.*
4. **Compile the Project**:
   Run the following command in your terminal within the project directory:
   ```bash
   dotnet build MultiVehicleDashPlugin.csproj -c Release
   ```
5. **Deploy**:
   The newly compiled file will be located at `bin\Release\net48\MultiVehicleDashPlugin.dll`. Copy this new `.dll` into Mission Planner's `plugins` folder and restart Mission Planner.

---
**Enjoy simplified multi-drone orchestration!**
