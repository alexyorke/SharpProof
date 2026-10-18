@echo off
call "%~dp0build.cmd" -restore %*
exit /b %errorlevel%
