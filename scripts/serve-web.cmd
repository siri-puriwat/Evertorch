@echo off
rem Serves the web build in artifacts\client\Web at http://localhost:8000 through serve-web.ps1 and opens it in the
rem default browser; Ctrl+C stops it. Extra arguments go to the script, e.g. -Port 8001.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0serve-web.ps1" %*
if errorlevel 1 (
    echo.
    echo The web build is not being served.
    pause
    exit /b 1
)
