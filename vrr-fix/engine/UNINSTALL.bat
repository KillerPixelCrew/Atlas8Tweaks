@echo off
rem Acer Atlas 8 VRR Fix - Uninstall. Extra arguments are passed through, e.g. -MinimumHz 30
net session >nul 2>&1
if errorlevel 1 (
    if "%~1"=="" (
        powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    ) else (
        powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    )
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AcerAtlasVrr.ps1" -Action Uninstall %*
echo.
pause
