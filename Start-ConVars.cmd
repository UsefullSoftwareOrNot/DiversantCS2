@echo off
setlocal
echo CameraProbe. Revision 2026-10-02-build-14188.
echo Engine version is detected automatically. Verified player profiles: 14186 and 14188.
echo F6: apply image commands, switch teams, then reapply and verify image commands.
echo Enable the developer console on the standard tilde key. No cfg is needed.
echo F7: record camera. F8: cancel recording.
echo Keep this window open. Ctrl+C restores owned local HP, camera timestamp and ConVar flags.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -EnableSwitching
pause
