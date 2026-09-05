@echo off
setlocal
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-UiAcceptance.ps1" %*
exit /b %errorlevel%
