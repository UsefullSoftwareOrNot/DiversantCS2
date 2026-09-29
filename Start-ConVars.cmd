@echo off
setlocal
echo Camera recovery + automatic LOCAL HP=10000 + F6/F7. Revision 2026-09-29-auto-hp.
echo F6: apply image commands, switch teams, then reapply and verify image commands.
echo Enable the developer console on the standard tilde key. No cfg is needed.
echo F7: record camera. F8: cancel recording.
echo Keep this window open. Ctrl+C restores owned local HP, camera timestamp and ConVar flags.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -EnableSwitching
pause
