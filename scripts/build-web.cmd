@echo off
rem Makes the development web build in artifacts\client\Web through build-web.ps1: refreshes both content packages,
rem then builds in Unity's batch mode. Close the Unity editor first. Run it from a terminal or double-click it.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-web.ps1" %*
if errorlevel 1 (
    echo.
    echo The web build failed.
    pause
    exit /b 1
)
