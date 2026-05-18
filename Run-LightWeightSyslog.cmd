@echo off
setlocal
set "APPDIR=%~dp0bin\Release\net48"
if not exist "%APPDIR%\LightWeightSyslog.exe" (
    echo Build output not found: "%APPDIR%\LightWeightSyslog.exe"
    echo Open this folder in a command prompt and run:
    echo   dotnet build LightWeightSyslog.csproj -c Release
    exit /b 1
)
start "" /D "%~dp0" "%APPDIR%\LightWeightSyslog.exe"
