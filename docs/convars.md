# Local ConVar mode — 2026-09-28

User requested local execution of `spec_freeze_time 1000` and `mat_fullbright 1` despite restrictions. Scope: change only the two local restriction bits; let normal console execution run the engine's callbacks. Keep diagnostics and switching modes read-only with respect to process memory.

Live observations in engine build 14185:

| Name | Type | Value | Flags | Owned bit |
|---|---|---|---|---|
| mat_fullbright | int32 (3) | 0 | 0x400004000 | 1 << 14 (cheat) |
| spec_freeze_time | float32 (7) | 3 | 0x28200C | 1 << 13 (replicated) |

VEngineCvar007 originally resolved from the pinned interfaces.json. Current CCvar has linked-list storage at +0x50, head at +0x58, count at +0x5E, capacity at +0x60. Nodes are 16 bytes: ConVarData pointer +0, previous ushort +8, next ushort +10. The observed 14185 list contains 4111 entries. ConVarData name pointer +0, type +0x28, flags +0x30, value +0x60.

On 2026-09-29 the installed engine updated to 14186. Public schema output remained 14185. Standalone ConVar mode uses a separate `ConVarLayout` profile: engine2.dll build RVA 6410728 and tier0.dll interface RVA 3851888 for 14185/14186. A complete read-only registry traversal on 14186 passed the list, bounds, vtable, identity, type and uniqueness checks; both target values and flags matched the table above. Standalone ConVar readers cannot capture player state. Player schemas were subsequently generated locally for 14186 (see reference/provenance.json). Unknown ConVar builds are rejected. Profiles must be rechecked against the running engine for later builds.

The combined `play` mode loads the validated 14186 player schema, then holds the ConVar session open while Recorder handles F6/F7. Cancellation is linked, and the ConVar session restores flags in its finally block after Recorder exits. Start-ConVars.cmd launches this combined mode. `cvars-unlock` remains available without hotkeys. Each F6 applies the numeric values through console input and validates a fresh readback transition before sending team commands. Startup only unlocks flags. The standard console key (~ / Ё) is required; F9/F10 binds and camera_probe.cfg are no longer used by the application.

On 2026-10-02 build 14188 was inspected from the installed game. `VEngineCvar007` remains at tier0.dll RVA 3851888, and a complete live registry traversal passed with the same two names, types, values and flags. The combined mode selects a separate 14188 player schema and exact camera/movement instruction profile. Build 14186 remains available through its earlier profile.

On 2026-10-04 SourceRevision 11076591 replaced client.dll without changing engine build 14188. Player schemas are now selected by engine build plus the loaded client.dll SHA-256. This prevents a same-build hotfix from silently reusing stale player globals. ConVar-only mode remains build-based because it validates the live registry structure and both target identities before every write.

On 2026-10-06 build 14189, SourceRevision 11087167 was inspected. `VEngineCvar007` remains at tier0.dll RVA 3851888. A complete live registry traversal again found the two target entries with the expected types and restriction flags before enabling the build.

Journal original state before writes; use a per-process mutex. Flush a temporary file, then publish it by a non-overwriting rename. Resolve names uniquely, validate identity and type before each write. Re-discover targets in the current registry before automatic recovery, and retain the Ctrl+C handler until recovery finishes. Restore only owned flag bits, preserve unrelated bits. Validate saved process/start time and live identities before crash recovery. Do not write numeric values directly: mat_fullbright has an engine callback and raw value writes would skip it.

Limitations: removing local flags may not bypass other engine checks. The server can update replicated values. Flag restoration does not restore numeric values changed through the console; print original-value commands. Remote reads/writes are not an atomic transaction with the engine; a concurrent engine change can race a write. No live command execution or visual recovery is claimed without observation.

Validation: policy tests first failed for six missing behaviors, then passed. A native write test uses only an allocated 24-byte buffer in the test process and checks both surrounding canaries. Live game inspection remains read-only.
