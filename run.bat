@echo off
:: Direct launcher for FigPin Layer Separator Studio
echo Building latest FigPin Application...
dotnet publish "%~dp0FigPin\FigPin\FigPin.csproj" -c Release -r win-x64 /p:WindowsPackageType=None -o "%~dp0dist"
if %ERRORLEVEL% EQU 0 (
    echo Launching FigPin...
    start "" "%~dp0dist\FigPin.exe"
) else (
    echo [ERROR] Failed to build FigPin application.
    pause
)
