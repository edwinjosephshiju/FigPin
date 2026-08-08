@echo off
title FigPin Studio 1-Click Installer
echo ======================================================================
echo              FigPin Studio 1-Click Package Installer
echo ======================================================================
echo Launching FigPin PowerShell Installer...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install_FigPin_Local.ps1"
echo.
pause
