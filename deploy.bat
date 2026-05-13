@echo off
echo ============================================
echo   Multi-Vehicle Dash - Deploy ^& Restart
echo ============================================

:: Kill Mission Planner if running
echo [1/3] Closing Mission Planner...
taskkill /IM MissionPlanner.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

:: Copy updated plugins
echo [2/3] Deploying plugins...
copy /Y "%~dp0MultiVehicleDashPlugin.cs" "C:\Program Files (x86)\Mission Planner\plugins\MultiVehicleDashPlugin.cs"
del /q "C:\Program Files (x86)\Mission Planner\plugins\SwarmControlPlugin.cs" 2>nul
if %errorlevel% neq 0 (
    echo ERROR: Failed to copy. Try running as Administrator.
    pause
    exit /b 1
)
echo Plugins deployed successfully.

:: Restart Mission Planner
echo [3/3] Starting Mission Planner...
start "" "C:\Program Files (x86)\Mission Planner\MissionPlanner.exe"

echo ============================================
echo   Done! Dashboard will auto-open in MP.
echo ============================================
timeout /t 3
