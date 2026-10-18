@echo off
pwsh -NoLogo -NoProfile -File "%~dp0build.ps1" %*
exit /b %errorlevel%
