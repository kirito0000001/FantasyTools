@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Pakout.ps1" %*
exit /b %ERRORLEVEL%
