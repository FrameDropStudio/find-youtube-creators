@echo off
setlocal

set "ROOT=%~dp0"
set "URL=http://127.0.0.1:5078"
set "DLL=.\FindYouTubeCreator.App\bin\Debug\net8.0\FindYouTubeCreator.App.dll"

cd /d "%ROOT%"

echo Building the web app...
dotnet build ".\FindYouTubeCreator.App\FindYouTubeCreator.App.csproj" --no-restore
if errorlevel 1 (
    echo.
    echo Build failed. Fix the error above and launch again.
    pause
    exit /b 1
)

dotnet "%DLL%" --url %URL%

endlocal
