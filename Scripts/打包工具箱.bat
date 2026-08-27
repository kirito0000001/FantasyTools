@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=powershell.exe"
where pwsh.exe >nul 2>nul
if "%ERRORLEVEL%"=="0" set "POWERSHELL_EXE=pwsh.exe"
echo Using %POWERSHELL_EXE%
%POWERSHELL_EXE% -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%%~n0.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Script failed. Press any key to close.
    pause >nul
    exit /b %EXIT_CODE%
)
echo.
echo Script finished. Press any key to close.
pause >nul
endlocal
