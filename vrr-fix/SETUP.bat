@echo off
setlocal EnableExtensions
title Acer Atlas 8 VRR Fix
set "A8DIR=%~dp0"
set "A8SELF=%~f0"
set "A8ENGINE=%~dp0engine\AcerAtlasVrr.ps1"

rem Double-clicking a file inside a ZIP only extracts that one file.
if not exist "%A8ENGINE%" (
    echo.
    echo  The other files of the fix were not found next to SETUP.bat.
    echo.
    echo  Please extract the whole ZIP first:
    echo    right-click the ZIP ^> "Extract All..." ^> open the extracted folder
    echo    and double-click SETUP.bat there.
    echo.
    pause
    exit /b 1
)

rem Ask for administrator rights once, then continue in the elevated window.
net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:A8SELF -Verb RunAs" >nul 2>&1
    if errorlevel 1 (
        echo.
        echo  Administrator rights are needed. Please run SETUP.bat again and click "Yes".
        echo.
        pause
    )
    exit /b
)

rem Remove the "downloaded from the internet" mark from the extracted files.
powershell.exe -NoProfile -Command "Get-ChildItem -LiteralPath $env:A8DIR -Recurse -File | Unblock-File" >nul 2>&1

:menu
cls
echo.
echo  ==========================================================
echo    Acer Atlas 8 VRR Fix  -  part of Atlas8Tweaks
echo    Extends the VRR range of the internal screen
echo    from 48-120 Hz to 28-120 Hz (Intel Arc graphics).
echo  ==========================================================
echo.
echo    [1]  Install / update  (recommended: 28-120 Hz)
echo    [2]  Install with a different minimum (20-47 Hz)
echo    [3]  Show status
echo    [4]  Open the VRR test tool
echo    [5]  Uninstall and restore the original settings
echo    [0]  Exit
echo.
choice /c 123450 /n /m "  Press a number: "
set "PICK=%errorlevel%"
if "%PICK%"=="1" goto install
if "%PICK%"=="2" goto custom
if "%PICK%"=="3" goto status
if "%PICK%"=="4" goto tester
if "%PICK%"=="5" goto uninstall
exit /b 0

:install
call :run -Action Install
goto menu

:custom
echo.
echo  Below about 28 Hz the screen may visibly flicker. 28 is recommended.
set "FLOOR="
set /p "FLOOR=  Minimum refresh rate in Hz (20-47): "
call :run -Action Install -MinimumHz "%FLOOR%"
goto menu

:status
call :run -Action Status
goto menu

:tester
if exist "%A8DIR%Tools\VrrTest.exe" (
    start "" "%A8DIR%Tools\VrrTest.exe"
) else (
    echo  Tools\VrrTest.exe was not found.
    pause
)
goto menu

:uninstall
echo.
choice /c YN /n /m "  Remove the fix and restore the original Intel settings? (Y/N) "
if errorlevel 2 goto menu
call :run -Action Uninstall
goto menu

:run
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%A8ENGINE%" %*
echo.
pause
exit /b
