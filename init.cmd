@echo off
REM init.ps1 を ExecutionPolicy を回避して起動するだけのラッパー。
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0init.ps1"
set EC=%errorlevel%
echo.
pause
exit /b %EC%
