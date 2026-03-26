@echo off
setlocal

set "ROOT=%~dp0"
set "URL=http://127.0.0.1:5078"

cd /d "%ROOT%"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET 8 SDK was not found on this machine.
    echo Install it from https://dotnet.microsoft.com/download and try again.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "try { Invoke-WebRequest -Uri '%URL%/api/defaults' -UseBasicParsing -TimeoutSec 2 | Out-Null; Start-Process '%URL%'; exit 0 } catch { exit 1 }"
if not errorlevel 1 (
    exit /b 0
)

start "FindYouTubeCreator Web App" cmd /k ""%ROOT%Run-WebApp.bat""

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$deadline = (Get-Date).AddSeconds(60);" ^
    "while ((Get-Date) -lt $deadline) {" ^
    "  try {" ^
    "    Invoke-WebRequest -Uri '%URL%/api/defaults' -UseBasicParsing -TimeoutSec 2 | Out-Null;" ^
    "    Start-Process '%URL%';" ^
    "    exit 0;" ^
    "  } catch { Start-Sleep -Milliseconds 750 }" ^
    "}" ^
    "Write-Host 'The web app did not start within 60 seconds. Check the FindYouTubeCreator Web App window for errors.';"

pause
endlocal
