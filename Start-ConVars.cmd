@echo off
setlocal
echo CameraProbe. Revision 2026-10-06-auto-discovery.
echo Engine version, client.dll fingerprint and required positions are detected automatically.
echo Reviewed profiles are preferred; routine new builds use guarded offline discovery.
echo F6: always recover image commands; switch teams only when T/CT freeze time is available.
echo Enable the developer console on the standard tilde key. No cfg is needed.
echo F7: record camera. F8: cancel recording.
echo Keep this window open. Ctrl+C restores owned local HP, camera timestamp and ConVar flags.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -EnableSwitching
pause
