@echo off
REM start.ps1 を ExecutionPolicy を回避して起動するだけのラッパー。
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1"
echo.
echo --- exited (code %errorlevel%) ---
pause
