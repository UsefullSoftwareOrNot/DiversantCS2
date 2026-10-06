# Verification — 2026-09-28

- Public schema pinned to `c46bfec6ac83b34fea4ce85383d9f0d555e96b38`, engine build 14185.
- Local game reports patch 1.41.8.5 in steam.inf; live engine memory reports 14185.
- Initial policy tests: 12 failed as expected against unimplemented guards. Implementation: 15/15 passed.
- Review found stale eligibility before first input, cancellation after capture, focus loss during delay, and missing intermediate-state evidence. Added execution tests: 5 failed before implementation, then 20/20 total passed.
- Installer test preserves quoted/compound bindings, restores absent bindings with unbind, and refuses existing files.
- A live read-only snapshot succeeded: team 0, no identified local pawn. This is not a failing-state capture or evidence of recovery.
- Windows screenshot tool failed with `FrameArrived timed out`; visual gameplay validation was unavailable.
- No team inputs or game-process memory writes were performed during validation.
- Two opt-in config files were installed in the game's cfg directory: camera_probe.cfg and camera_probe_restore.cfg. They have not been activated by this session.

## Outstanding

Camera recovery remains unimplemented. Hotkey delivery has not been tested in a live match. Entity/service reuse at the same address is not excluded by the non-atomic reader. Polling cannot detect focus changes shorter than its sampling interval.

## ConVar extension

- Live read-only lookup uniquely found mat_fullbright (int32, 0, flags 0x400004000) and spec_freeze_time (float32, 3, flags 0x28200C).
- Six initial policy tests failed against the unimplemented methods, then passed with the scoped masks.
- Added native WriteProcessMemory test against the test process only; surrounding bytes remained unchanged.
- Review corrections: re-discover current registry entries before automatic restoration, retain the Ctrl+C handler through restoration, publish the durable journal by atomic non-overwriting rename.
- Added tests for stale registry identities, partial rollback failure, failed verification, incomplete journal writes and existing-journal protection. Full suite: 32/32.
- Live flag writes, console command acceptance and visual outcome remain unverified. No anti-cheat compatibility or account-safety guarantee is made.

## Build 14186 compatibility — 2026-09-29

- steam.inf reports 1.41.8.6, SourceRevision 11049427; live engine reports 14186.
- GitHub API still reports the pinned 14185 schema commit; reference files were not relabelled.
- Isolated ConVar compatibility from player schemas. The same engine build and tier0 interface RVAs resolved on 14186. The complete registry traversal passed; names, types, values and flags matched the previous observations.
- Published the updated application. `Run.ps1 -ConVars Inspect` exits 0 on live 14186 and reports both variables, values 0/3 and flags 0x400004000/0x28200C.
- `Run.ps1 -Snapshot` still exits 1 with the 14185 player-schema mismatch, as intended.
- Two new compatibility tests first failed to compile without the profile, then the full suite passed 34/34. Unknown builds remain rejected.
- No CS2 writes or command execution were performed in this validation. Rendering recovery remains unverified.
- Installed module SHA256 for this observation (provenance only, not runtime hash enforcement):
  - engine2.dll: `B63B2AB7CAE8115E9BF05562E3289E2AA9E65D3798633CC160B653BC55EE6B52`
  - tier0.dll: `4E0DCB0AF3F6953F37DDAED0F4E67A56D031F1E84964A262148F8A6F80547791`

## F6 and camera diagnostics — 2026-09-29

- User confirmed the two ConVars work, then reported no F6 response and no camera rotation only in the bug state.
- Causes found for F6: cvars-unlock did not run the hotkey loop, and watch still used the 14185 player schema. Added combined play mode and updated Start-ConVars.cmd.
- Downloaded official cs2-dumper 0.1.3; SHA256 matched its GitHub release digest. Its native OS layer failed in the restricted environment, then succeeded outside it. The tool generated 14186 schemas, interfaces and offsets. Two unused patterns failed: dwSensitivity and dwSoundSystem_engineViewData. None of our required offsets was missing. Provenance is recorded in reference/provenance.json.
- Separated lightweight switch eligibility from optional camera reads. Freeze flag and round counter are also rechecked at the end of the read. F6 records a best-effort camera baseline before obtaining fresh input eligibility.
- Camera diagnostics resolve the local controller's active pawn through the entity registry, validating the full handle, identity flags and backpointer. This avoids relying on the prediction pawn when observing. In the live menu the resolved pawn is c_cs_observer_for_precache; its game-specific fields are intentionally skipped with a warning. This is not a bug-state capture.
- Added input/pawn view angles, forced/previous observer mode and current ConVar values to captures. Reviewer caught an assignment of non-finite angles before validation; fixed by validating a temporary array first.
- Tests: 37/37 passed, including stale entity handle rejection. No game memory writes or team input were performed by the assistant during this verification.
- User reported a Steam login disconnect; process exit was observed, but its cause was not determined. The game subsequently restarted and live snapshot reading succeeded.
- User cannot currently test F6 or record the bug state. Actual team switching and rotation recovery remain unverified; no rotation patch has been implemented.

## F6 opens quit prompt / image commands on F6 — 2026-09-29

- User later provided F6/F7 captures. The F6 log shows virtual key 121 (F10), then 120 (F9). Installed user_keys_default.vcfg assigns F10 to cs_quit_prompt. The live target binds had not replaced that default. Both captures still report numeric ConVar values 0/3 despite unlocked flags.
- Removed F9/F10 input and cfg dependency. F6 now opens the standard console, applies image commands, confirms values, sends jointeam for the other team and back, then hides the console/menu. Both launchers select play --enable-switching; -EnableSwitching also selects play. F6 in standalone watch is rejected with instructions to use play.
- Console verification requires a fresh spec_freeze_time transition to 1001/1002 and then the requested 1000; fullbright must read 1. Already enabled values cannot falsely acknowledge an unopened console. Cancellation can leave the intermediate numeric value; the application reports the recovery command and journals the current values when readable. It sends no cleanup input after cancellation or focus loss.
- Focus/modifier/cancellation checks guard typing; team/round/freeze are checked again immediately before Enter. Key release failures receive one retry and are reported. Camera sampling gets its own eight-second deadline after the sequence.
- New tests first failed without implementation, then the full suite passed 41/41. Build/publish and CLI help succeeded. Actual console automation and team switching were not exercised by the assistant in a live match; user closed CS2 before publication. Rotation remains unresolved.

## Console Enter and actual pawn type — 2026-09-29

- New user captures at 01:03/01:04 UTC show image application failing with unchanged values 0/3; user confirmed that manual Enter on the automatically typed line makes it execute.
- Submission now waits 120ms after typing, runs the final state guard, sends physical Enter (scan 0x1C) held for 80ms, then waits 50ms before reusing the input line. Tests check ordering and cancellation before Enter. These timings and scan-code delivery still require a live F6 test.
- Fixed diagnostic rejection of c_cs_player_for_precache/c_cs_observer_for_precache: bounded MSVC x64 RTTI checks now establish C_CSPlayerPawn or C_CSObserverPawn before reading camera fields. Designer names are descriptive only. The complete-object locator self-RVA and all metadata locations are checked against client.dll bounds; local controller ownership remains required.
- Added view direction derived from the fourth row of the view-projection matrix. A live read at 01:19:37 UTC had matching input/pawn angles [28.730465,-4.454125,0], while the projected view direction was approximately [0,112,0]; local pawn health0, life state2. This corroborates the user's reported separation of character and camera. It does not establish the rendering fix.
- Source reviewer confirmed the Enter ordering and updater, and identified a lingering null-designer-name gate after RTTI validation; removed it.
- Full C# suite: 43/43. Pending-update test verifies locked DLL rejection before reference replacement, successful installation after release, journal preservation and repeat no-op. The staged application and help command build successfully.
- Running old CameraProbe locks its DLL. Updated launchers apply the staged release on next launch after the user stops the old app with Ctrl+C; game restart is unnecessary. No camera memory writes or game inputs were performed during this investigation.

## Camera branch experiment — 2026-09-29

- Inspected the installed client.dll with a read-only Capstone script (`artifacts/inspect_camera.py`). CCSPlayer_CameraServices virtual slot 34 starts at RVA 0x882F50. It calls the pawn's IsAlive virtual method, then compares elapsed time since pawn+0x1458 (schema m_flDeathTime) before calling the death-camera path at 0x8822C0. The normal path beginning 0x882FFA reads the pawn's eye position and angles. The pawn IsAlive implementation at 0x1648E0 tests byte+0x354 (schema m_lifeState).
- The experiment validates build 14186, schema offset 5208, and exact instruction bytes before applying a four-byte timestamp change. It is restricted to the observed HP>0, LifeState=2, PawnIsAlive=false inconsistency on a validated local C_CSPlayerPawn. It does not change health, life state, team or executable code.
- First successful experiment: `src/CameraProbe/bin/Release/net10.0/captures/camera-experiment-20260929-015249-76f16eeb68954cafa1b371ab86882a63.jsonl`. The four-second change from deathTime 86.015625 to 1e9 moved rendered yaw from 90 to -41.944817 and pitch from approximately 0 to -28.154795, matching input/pawn angles. Original timestamp 86.015625 was restored and the journal closed. The input did not move during this run; continuous tracking remains to be tested.
- Before that successful experiment: a byte-signature length bug rejected the operation before any write and was fixed by deriving lengths from the expected byte arrays; restricted process access was denied before any write, then the approved execution path was used. Attempts after the pawn died or disappeared were refused before mutation.
- Reviewer identified cancellation and partial-write recovery issues. Added a cancellation check immediately before application and retained uncertain recovery journals when an interrupted write leaves an unexplained value. Added schema-offset verification before application.
- F6 now reapplies and verifies the two image commands after the team round trip, in addition to its console-channel verification before switching. A test simulates values reset by team switching. A live F6 test is still required. Combined play startup restores the previous ConVar journal before reading fresh entries for the next session.
- Second successful timestamp experiment: `camera-experiment-20260929-015937-bd94daccd32146b4a553b8bb99f4bdd2.jsonl` under the same build-output captures directory. Over 15 seconds, 240 of 243 samples matched input yaw 67.54962 after transition from frozen yaw -133. Input remained constant again, so this still does not demonstrate continuous mouse tracking. DeathTime 443.375 was restored.
- Added maintained recovery to `play` using a separate GameReader, matched to the main process PID/start time. The worker holds the timestamp while the observed inconsistent state remains, restores on exit, and watches for the next occurrence. Linked cancellation joins the worker before disposing the readers. The standalone maintained-mode smoke run was cancelled while waiting; no camera journal was created in that run.
- Repeated stable evidence of entity serial replacement/deletion retires an old recovery journal without writing to a recycled object. Uncertain reads are retried, and unresolved uncertainty retains the journal. Transient captures end the current hold and permit guarded cleanup; optional final snapshots do not terminate recovery.
- Latest verification: 49/49 C# tests; pending-update and config-installer PowerShell tests passed. Staged publication succeeded. Installing over the running old DLL was rejected as intended; the user must stop that window and relaunch to apply revision `2026-09-29-camera`. Continuous camera tracking and complete F6 automation await this live verification.

## Recovery after health reaches zero — 2026-09-29

- Logs `camera-experiment-20260929-053749-07da782fbb5045a0af8d7ee05539ca87.jsonl` and `camera-experiment-20260929-054019-acdf1afa8c164148b779fb5ed10d65ee.jsonl` show recovery ending at HP=0 while the same player pawn remains active. Live read at 05:46 UTC confirms HP=0 and mismatched input/rendered camera angles.
- Maintained recovery now retains eligibility after health depletion only when this process previously observed the bug with positive health on the same full handle/team/round. Active transactions close before team/round changes, including changes at positive health. Initial admission of ordinary dead players remains rejected.
- Added automatic read-only movement captures at HP=0. No movement, health or life-state override was added. Standalone movement recording timed out without a foreground Space press; movement failure remains unresolved.
- Regression test reproduced the old rejection at HP=0, then passed. Final suite: 52/52. Release publish succeeded. Update is staged with a checksum; installation is blocked by the running old DLL and requires Ctrl+C/relaunch. Live post-death outcome remains unverified.

## Movement after HP=0 — 2026-09-29 05:54 UTC

User confirms camera remains active after health depletion, but movement stops and damage can still be dealt.

Capture: `artifacts/CameraProbe/captures/camera-experiment-20260929-055309-9bc2949e3fde43f3a61397490f9ff259.jsonl`.
117 movement samples; 106 precede freeze time. Foreground W presses produce ForwardMove=1, button changes and advancing LastCommand values (4602 through 6661). Position remains approximately (-113.2168, -2001.1903, -167.96875); local and server-velocity fields are zero. These are client-memory observations, not a server trace.

Read-only inspection of installed client.dll finds CCSPlayer_MovementServices vtable RVA 0x1c0df48, slot50 at RVA0x8c4c70. A nearby chained block at 0x8c529e compares pawn health at +0x34c with zero; its zero/negative branch clears three adjacent 32-bit values at a working-data pointer +0x2c/+0x30/+0x34 unless an observer-mode condition applies. Further health checks occur at 0x8c3860 and 0x8c9a17. The exact identity of the cleared working-data fields and server acceptance of movement have not been independently verified. This supports investigating health-dependent movement processing, not claiming a completed fix.

No health/life-state/movement writes or executable patches were made. A local health override has not been validated as a repair and would not by itself demonstrate server-authoritative movement.

## Automatic local HP=10000 in play — 2026-09-29

Requested by the user. Main play now runs camera and movement workers with separate readers, matching process identity, shared cancellation and joined cleanup before ConVar restoration. Movement observes eligible HP=0 automatically and writes local HP=10000; no key or 15-second deadline in automatic mode. Standalone mode remains time-limited. Journals store the applied value and recognize legacy HP=1 journals.

Transient observations after a completed write end the current hold and restore before waiting again. The final catch covers `!attempted || completed`; uncertain partial writes and cleanup failures remain fatal. Review identified this transition issue and it was repaired before final packaging.

55/55 tests passed; both Release packages published. The main package is staged with ready.sha256. Installation was attempted but blocked by the running old DLL; Ctrl+C and launcher restart are required. Server movement outcome remains unverified; no live health write was performed by the assistant.

## Build 14188 compatibility — 2026-10-02

- `steam.inf` reports patch 1.41.8.8, SourceRevision 11064488; live signature discovery reports engine build 14188.
- A fresh local cs2-dumper 0.1.3 run produced the 14188 schema and global offsets. All fields used by CameraProbe match the 14186 field layout; changed globals are isolated in the new build profile.
- The ConVar interface remains at tier0.dll RVA 3851888. A live read-only registry traversal found unique `mat_fullbright` and `spec_freeze_time` entries with the expected types, values and restriction flags.
- Read-only inspection found the camera validation blocks at client.dll RVAs 0x8827BC, 0x8827E7 and 0x88280C, and the health comparison at RVA 0x8C4ADE. Exact bytes were verified against the loaded module through the restore-only commands before publication.
- Live `snapshot` completed with engine build 14188 and no camera warnings. The full C# suite passed 58/58 before publication.

## Build 14188 hotfix fingerprint — 2026-10-04

- Valve kept engine build 14188 but changed SourceRevision from 11064488 to 11076591 and replaced client.dll. The previous build-only profile read four stale global offsets, producing team 0 and an invalid entity pointer.
- Build 14188 player profiles are now selected by both engine build and the verified client.dll SHA-256. Unknown same-build hotfixes are rejected instead of reusing stale offsets. The archived 14186 metadata predates client hashing and retains its explicit build-only fallback.
- The new dump changes `dwCSGOInput`, `dwEntityList`, `dwGameEntitySystem`, and `dwViewAngles` by 16 bytes. Schema fields and the inspected camera/movement instruction blocks remain unchanged.

## Build 14189 compatibility — 2026-10-06

- `steam.inf` reports patch 1.41.8.9, SourceRevision 11087167; live signature discovery reports engine build 14189.
- A fresh local cs2-dumper 0.1.3 run produced the fingerprinted 14189 schema for client.dll SHA-256 `7081d87fd49961a5d82e45abd0f0b8f8f05deeb3cb849837c1759716221c6977`. Player field offsets remain unchanged; the client globals used by CameraProbe moved.
- `VEngineCvar007` remains at tier0.dll RVA 3851888. A complete live registry traversal validated unique `mat_fullbright` and `spec_freeze_time` entries with the expected types and restriction flags.
- Camera entry/death-load RVAs remain 0x8827BC/0x8827E7, the camera comparison remains at 0x88280C with an updated RIP-relative displacement, and the movement health comparison remains at 0x8C4ADE. Restore-only commands verified the exact bytes against the loaded module.
- Live `snapshot` completed on build 14189 with no warnings. The full C# suite passed 60/60.
