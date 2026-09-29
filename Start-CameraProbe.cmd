@echo off
setlocal
echo Camera recovery + automatic LOCAL HP=10000. Revision 2026-09-29-auto-hp.
echo F6: apply image commands, switch teams, then reapply. Console key: tilde. No cfg required.
echo F7: capture only. F8: cancel recording. Ctrl+C: restore camera timestamp and ConVar flags, then exit.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -EnableSwitching
pause
