# CS2 Camera Probe

## Requested outcome
A Windows C# application switches from either playing team to the other team and back during freeze time, then restores the missing world image. HUD remains visible; the image returns when freeze time ends.

## Evidence and current limit
CS2 1.41.8.5 is installed. Public a2x/cs2-dumper schema commit c46bfec6ac83b34fea4ce85383d9f0d555e96b38 describes engine build 14185. These are different numbering systems: the running engine build must be checked before interpreting memory. The user's quoted camera fix has no identified source or verified mechanism. Camera writes must not be invented from field names.

## Implementation

The later user-requested ConVar extension is specified in [convars.md](convars.md). It adds explicit flag-write modes; the snapshot/watch implementation below retains its read-only behavior.
.NET 10 x64 console app, no external packages. Read only the local player's controller, pawn, camera and freeze state through documented Win32 process APIs. Reject an engine build mismatch. Store local JSONL observations; never collect player names, Steam IDs, other players or whole memory dumps.

F6 sends two configured team key presses only while CS2 is foreground, the snapshot is valid and freeze time is active. F9 and F10 are opt-in CS2 bindings for T and CT, supplied in a separate cfg. Preserve existing bindings with a generated restore cfg. F7 captures camera state without team input. F8 cancels collection. Debounce held keys; never run a second macro concurrently. Log each input attempt separately from observed team state.

## Recovery research gate
This first implementation captures the failing transition. It does not claim to fix the camera and does not write process memory. Actual recovery remains incomplete until a failing camera state and successful corrective action are observed. A dump's field names alone do not establish a fix.

## Validation
Build in Release. Exercise decoding and macro gating with a dependency-free test runner. Run the snapshot command against the running game. Visual confirmation and a failing-state capture are needed before claiming recovery.
