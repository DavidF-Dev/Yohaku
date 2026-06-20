@echo off
REM Double-clickable launcher: builds (Release) and starts Yohaku in the tray.
REM Pass -NoBuild to skip the build, e.g.  run.bat -NoBuild
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
