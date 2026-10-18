@echo off
call "%~dp0build.cmd" -restore -build -test -integrationTest %*
exit /b %errorlevel%
