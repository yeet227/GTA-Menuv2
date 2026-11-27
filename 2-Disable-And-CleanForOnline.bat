@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Manage-StoryTrainer.ps1" -Action Remove
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" echo [ERROR] Script exited with code %EXITCODE%.
echo.
pause
exit /b %EXITCODE%
