@echo off
REM Launches Eden.Viewer in JOIN mode. Edit GODOT below to match your install.
set "GODOT=C:\Program Files\Godot\Godot_v4.6.1-stable_mono_win64.exe"

set EDEN_MODE=join
set EDEN_HOST=localhost
set EDEN_PORT=5001

pushd "%~dp0"
"%GODOT%" --path "%CD%"
popd
pause
