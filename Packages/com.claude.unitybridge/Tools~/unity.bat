@echo off
rem Client CLI di Claude Unity Bridge: vedi "unity help"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unity.ps1" %*
exit /b %ERRORLEVEL%
