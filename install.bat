@echo off
REM Batman: Arkham City - Archipelago installer
REM Windows only. Batman: Arkham City Game of the Year Edition only.
REM
REM Double-click this, or run install.ps1 directly from PowerShell if you
REM want to pass options such as -Check, -DryRun or -GamePath.

echo.
echo Batman: Arkham City - Archipelago installer
echo.
echo This will show you everything it intends to do and then ask before
echo changing anything.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

echo.
pause
