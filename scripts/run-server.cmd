@echo off
rem Starts the game server for local development through run-server.ps1: refreshes both content packages, starts
rem and migrates the Compose PostgreSQL database, then runs the server in the Development environment.
rem Double-click it, or run it from a terminal; extra arguments go to the server, e.g. --Network:Port=7778.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-server.ps1" %*
if errorlevel 1 (
    echo.
    echo The server did not start or stopped with an error.
    pause
    exit /b 1
)
