@echo off
call "%~dp0..\build.cmd" -ci -restore -build -test -integrationTest %*
exit /b %errorlevel%
