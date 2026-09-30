@echo off
rem Serves the web build in artifacts\client\Web through serve-web.ps1 on the first free port from 8000, and opens it
rem in the default browser; Ctrl+C stops it. Extra arguments go to the script, e.g. -Port 8765.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0serve-web.ps1" %*
if errorlevel 1 (
    echo.
    echo The web build is not being served.
    pause
    exit /b 1
)
