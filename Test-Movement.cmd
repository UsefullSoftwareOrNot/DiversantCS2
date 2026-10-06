@echo off
setlocal
echo Experimental LOCAL health check. Revision 2026-10-06-build-14189.
echo Engine version and client.dll fingerprint are detected automatically.
echo Leave CameraProbe running. At HP=0 outside freeze time, switch to CS2 and press Space.
echo Waiting has no timeout. Keep this window open while reproducing the bug.
echo Local HP=10000 for up to 15 seconds. Do not run with the new automatic HP mode.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test-Movement.ps1"
pause
