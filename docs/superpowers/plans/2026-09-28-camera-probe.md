# Camera Probe Implementation Plan

> Execute inline using the executing-plans workflow. User requested independent investigation and implementation; proceed without another design approval.

**Goal:** Build the usable switching and diagnostic portion, then investigate camera recovery from evidence.
**Architecture:** Read-only Win32 reader, versioned JSON schema, guarded input sequence and JSONL recorder.
**Tech Stack:** .NET 10, C#, Windows x64, no NuGet dependencies.
**Spec:** docs/design.md

## Global constraints
- Reject unsupported builds and unavailable local player state.
- Input only when CS2 is foreground; require freeze time to start switching.
- Never describe macro delivery or successful memory reads as a camera fix.

## Tasks
- [x] Tests: unsupported build, foreground and freeze gates, spectator rejection, CT/T symmetry, invalid delay, invalid pointers, short read, non-finite vectors; execution scenarios for cancellation and focus changes.
- [x] Reader: checked reads with process handle cleanup, local player snapshot, separate optional camera errors.
- [x] Recorder and hotkeys: F6 sequence, F7 observation, F8 cancellation, bounded logs, no reentry.
- [x] Configuration: generate opt-in team bindings and restoration of the existing bindings.
- [x] Build, run tests, inspect live snapshot, review and document remaining recovery limitation.
- [ ] Capture the black-world state in a match and establish a causal camera recovery operation. No recovery is claimed.

## Review focus
Build drift, process exit, focus loss between keys, entity reuse, and cancellation must not produce blind input or misleading evidence.
