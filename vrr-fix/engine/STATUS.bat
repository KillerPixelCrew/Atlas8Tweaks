@echo off
rem Acer Atlas 8 VRR Fix - Status. Extra arguments are passed through, e.g. -MinimumHz 30
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AcerAtlasVrr.ps1" -Action Status %*
echo.
pause
