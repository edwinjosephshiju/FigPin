@echo off
:: Direct launcher for FigPin Layer Separator Studio
if exist "%~dp0dist\FigPin.exe" (
    start "" "%~dp0dist\FigPin.exe"
) else (
    echo Building FigPin Application...
    dotnet publish "%~dp0FigPin\FigPin\FigPin.csproj" -c Release -r win-x64 -o "%~dp0dist"
    start "" "%~dp0dist\FigPin.exe"
)
