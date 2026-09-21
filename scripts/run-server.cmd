@echo off
rem Starts the game server for local development: refreshes both content packages (so the Unity client's copy
rem always matches the server's), then builds and runs the server in the Development environment.
rem Double-click it, or run it from a terminal; extra arguments go to the server, e.g. --Network:Port=7778.
setlocal
cd /d "%~dp0.."

dotnet run --project Evertorch.Tools -- content build --client-out Evertorch.Client/Assets/StreamingAssets/GameData
if errorlevel 1 (
    echo.
    echo The content build failed; the server was not started.
    pause
    exit /b 1
)

dotnet run --project Evertorch.Server --launch-profile Development -- %*
if errorlevel 1 (
    echo.
    echo The server stopped with an error.
    pause
    exit /b 1
)
