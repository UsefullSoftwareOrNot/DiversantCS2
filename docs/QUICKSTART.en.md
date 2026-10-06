# Getting started — English

[Русский](QUICKSTART.ru.md) · [README](../README.md)

## Requirements

- Windows x64.
- .NET 10 SDK: `dotnet --list-sdks` must include a `10.*` version.
- CS2 engine build **14186**, **14188**, or **14189** for the main mode. Other builds are intentionally rejected.
- Git for cloning. Alternatively, download and extract the `main` branch ZIP from GitHub.

## Download and build

Open PowerShell:

```powershell
git clone --branch main https://github.com/UsefullSoftwareOrNot/DiversantCS2.git
cd DiversantCS2
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

The script restores dependencies, runs tests, and publishes the application to `artifacts/CameraProbe/`. Compiled DLLs are not committed; build the project first. Before rebuilding, close any running CameraProbe instance with **Ctrl+C** to release its DLL.

## Run

1. Start CS2 and enable the developer console. Keep its default **tilde (`~`)** binding.
2. Open **`Start-ConVars.cmd`** in the project root. `Start-CameraProbe.cmd` launches the same main mode; use one window.
3. To switch teams during freeze time, press **F6** while on T/CT. Close chat, console, and menus first. Avoid other key presses while the program enters commands.
4. To exit normally, press **Ctrl+C in the CameraProbe window** and wait for restoration to finish.

PowerShell alternative:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run.ps1 -EnableSwitching
```

## Controls and behavior

| Key | Action |
| --- | --- |
| F6 | Always apply and verify image commands. During T/CT freeze time, also switch teams and return, then reapply the image commands. |
| F7 | Record camera state for 8 seconds. |
| F8 | Cancel the current recording or switching sequence. |
| Ctrl+C in the program window | Exit and restore recorded changes. |

F6 uses `spec_freeze_time 1000` and `mat_fullbright 1`. No `camera_probe` configuration is required.

Camera recovery starts automatically for the recognized bug state. At HP=0, with camera recovery active and outside freeze time, the main mode experimentally sets **local HP=10000**. A separate `Test-Movement.cmd` session or Space press is not required.

This does not change server health. Restoration of server-accepted movement is **unconfirmed**. The game may overwrite local health. On context changes, the program checks whether restoration is valid and retains a journal if the result is ambiguous. The numeric values of the two ConVars are not automatically reset on exit.

## Diagnostics and updates

- Main-mode logs: `artifacts/CameraProbe/captures/`.
- One state snapshot: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run.ps1 -Snapshot`.
- If the DLL is locked, close the old CameraProbe window with Ctrl+C. CS2 can remain open.
- If an update is staged in `artifacts/CameraProbe.update/`, the next main `.cmd` launch installs it.
- If the game build is unsupported, do not just edit the schema build number: offsets and machine-code checks need separate verification.
- To update from Git, close CameraProbe, run `git pull --ff-only`, and repeat the build command.
