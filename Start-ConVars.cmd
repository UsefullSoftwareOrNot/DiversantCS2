@echo off
setlocal
echo CameraProbe. Revision 2026-10-04-hotfix-fingerprint.
echo Engine version and client.dll fingerprint are detected automatically.
echo Verified player profiles: 14186 and both known 14188 revisions.
echo F6: always recover image commands; switch teams only when T/CT freeze time is available.
echo Enable the developer console on the standard tilde key. No cfg is needed.
echo F7: record camera. F8: cancel recording.
echo Keep this window open. Ctrl+C restores owned local HP, camera timestamp and ConVar flags.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -EnableSwitching
pause
