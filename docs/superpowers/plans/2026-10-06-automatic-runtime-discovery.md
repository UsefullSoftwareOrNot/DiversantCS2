# Automatic Runtime Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let CameraProbe discover compatible CS2 schema, global, ConVar, camera, and movement positions locally for unknown builds while refusing ambiguous or structurally changed versions before any write.

**Architecture:** Exact reviewed profiles remain the fast path. Unknown fingerprints use a pinned bundled cs2-dumper to create an atomic local profile, while CameraProbe independently discovers guarded code blocks from loaded executable sections and combines all validated values in one immutable compatibility context.

**Tech Stack:** .NET 10/C# x64, Windows process APIs already used by CameraProbe, a2x/cs2-dumper 0.1.3, JSON, SHA-256, existing console test harness.

**Spec:** `docs/superpowers/specs/2026-10-06-automatic-runtime-discovery-design.md`

## Global Constraints

- Automatic discovery performs no network requests.
- The bundled dumper must match SHA-256 `501368ffb8f252b3cfb70fee6177b6ab4bbb21cad480724af17720d74ce44bbb` before execution.
- Every ambiguous pattern, missing field, process restart, changed DLL, timeout, or malformed output fails before writes or synthetic input.
- Exact reviewed profiles and legacy build 14186 behavior remain supported.
- Automatic profiles live below `captures/discovery/profiles/<build>/<client-sha256>/` and are published atomically.
- Runtime code checks remain exact immediately before camera or movement writes.

## Review Focus

- A second matching code pattern must reject discovery rather than selecting the first match; Task 1 tests duplicate matches.
- A game restart during dumper execution must reject the output; Task 2 tests changed process identity.
- A cache directory left by a crash must not become authoritative; Task 2 tests incomplete output and atomic publication.
- An unknown build with familiar field names but a changed instruction field displacement must fail; Task 1 tests displacement mismatch.
- Existing recovery journals must remain recoverable only through reviewed layouts, while new journals bind to module/layout fingerprints; Task 4 tests both paths.

---

### Task 1: Masked instruction discovery

**Files:**
- Create: `src/CameraProbe/BytePattern.cs`
- Create: `src/CameraProbe/PlayerCodeLocator.cs`
- Modify: `src/CameraProbe/PlayerCodeLayout.cs`
- Modify: `tests/CameraProbe.Tests/Program.cs`

**Interfaces:**
- Produces: `BytePattern.Parse(string)`, `BytePattern.FindUnique(IEnumerable<(int Rva, byte[] Code)>)`, and `PlayerCodeLocator.Locate(IEnumerable<(int Rva, byte[] Code)>, int deathTimeOffset, int healthOffset) -> PlayerCodeLayout`.
- Produces: `PlayerCodeLayout.Fingerprint` as SHA-256 over ordered RVAs and exact discovered bytes.
- Consumes: executable section snapshots already assembled by `GameReader`.

- [ ] **Step 1: Write failing locator tests**

Add tests for unique masked matches, no match, duplicate matches, out-of-range relative targets, camera death-field displacement mismatch, movement health-field displacement mismatch, and successful 14189 fixture discovery with RVAs `0x8827BC`, `0x8827E7`, `0x88280C`, and `0x8C4ADE`.

- [ ] **Step 2: Run the suite and verify RED**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: compilation fails because `BytePattern` and `PlayerCodeLocator` do not exist.

- [ ] **Step 3: Implement pattern and code locators**

`BytePattern` accepts two-digit hex bytes and `??` wildcards, scans section-local bytes, and requires one match. `PlayerCodeLocator` anchors on the unique camera entry, searches its bounded neighborhood for death-load and compare shapes, validates decoded field displacements and in-module relative targets, and locates the unique movement-health shape. Return exact matched bytes for the existing last-moment verifier.

- [ ] **Step 4: Run the suite and verify GREEN**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: all tests pass.

- [ ] **Step 5: Commit**

Commit message: `Add guarded runtime code discovery`

### Task 2: Pinned local dumper and atomic automatic-profile cache

**Files:**
- Create: `src/CameraProbe/AutomaticProfile.cs`
- Create: `src/CameraProbe/DumperRunner.cs`
- Create: `tools/cs2-dumper.exe`
- Create: `tools/cs2-dumper-LICENSE`
- Create: `tools/cs2-dumper-provenance.json`
- Modify: `src/CameraProbe/CameraProbe.csproj`
- Modify: `src/CameraProbe/ReferenceProfile.cs`
- Modify: `tests/CameraProbe.Tests/Program.cs`

**Interfaces:**
- Consumes: build, client SHA-256, process ID/start ticks, and a callback that rechecks live identity after discovery.
- Produces: `ReferenceProfile.ResolveOrDiscover(...) -> ResolvedProfile`, whose record contains path and `ProfileSource` (`Reviewed` or `Automatic`).
- Produces: `DumperRunner.Run(string toolPath, string outputDirectory, TimeSpan timeout) -> DumperResult`.

- [ ] **Step 1: Write failing profile tests**

Add tests for pinned-tool hash mismatch, nonzero dumper exit, timeout, changed process identity, missing required JSON, mismatched build, malformed/out-of-range required values, incomplete cache reuse, successful atomic publication, valid cache reuse, and exact reviewed-profile preference.

- [ ] **Step 2: Run the suite and verify RED**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: compilation fails because automatic-profile interfaces do not exist.

- [ ] **Step 3: Implement discovery and packaging**

Run the pinned executable directly with `--file-types json --output <temp> --no-log-file`, redirected output, and a 30-second timeout. Validate only required JSON and required names/ranges, write provenance, flush files, and publish by non-overwriting directory rename. Add the tool and license to build/publish output under `tools/`.

- [ ] **Step 4: Run the suite and verify GREEN**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: all tests pass, including injected fake-runner cases without launching CS2.

- [ ] **Step 5: Commit**

Commit message: `Add pinned automatic profile discovery`

### Task 3: Unified compatibility context

**Files:**
- Create: `src/CameraProbe/CompatibilityContext.cs`
- Modify: `src/CameraProbe/GameReader.cs`
- Modify: `src/CameraProbe/ConVarReader.cs`
- Modify: `src/CameraProbe/ConVarLayout.cs`
- Modify: `src/CameraProbe/ReferenceProfile.cs`
- Modify: `tests/CameraProbe.Tests/Program.cs`

**Interfaces:**
- Consumes: `ResolvedProfile`, loaded module sections, process identity, and client image identity.
- Produces: immutable `CompatibilityContext` with build, client hash, schema/offset documents, ConVar interface RVA, `PlayerCodeLayout`, layout fingerprint, and profile source.
- Produces: `GameReader.CodeLayoutFingerprint`, `GameReader.ClientSha256`, and `GameReader.VerifyCompatibilityIdentity(...)` for recovery workers.

- [ ] **Step 1: Write failing context tests**

Add tests for required-field extraction, interface extraction, negative and module-out-of-range RVAs, reviewed/automatic equivalence, dynamic ConVar selection, reader-context equality, and rejection when process/module identity changes.

- [ ] **Step 2: Run the suite and verify RED**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: compilation fails because `CompatibilityContext` does not exist.

- [ ] **Step 3: Integrate the context**

Build one context during `GameReader` startup. Replace direct JSON lookups and build-only code-layout selection with context values. `ConVarReader` uses the context interface RVA; build-only `ConVarLayout` remains solely for the documented legacy ConVar-only path. Recheck exact discovered code bytes before guarded writes.

- [ ] **Step 4: Run unit and live read-only verification**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Run: `dotnet artifacts/CameraProbe/CameraProbe.dll snapshot`, `cvars`, `camera-restore`, and `movement-restore` after publishing.

Expected: all tests pass; current build snapshot has no warnings; all three validation commands exit 0.

- [ ] **Step 5: Commit**

Commit message: `Use a unified runtime compatibility context`

### Task 4: Bind recovery journals to discovered layouts

**Files:**
- Modify: `src/CameraProbe/CameraExperiment.cs`
- Modify: `src/CameraProbe/MovementExperiment.cs`
- Modify: `src/CameraProbe/GameReader.cs`
- Modify: `tests/CameraProbe.Tests/Program.cs`

**Interfaces:**
- Consumes: `GameReader.ClientSha256` and `GameReader.CodeLayoutFingerprint` from Task 3.
- Produces: new journal fields `ClientSha256` and `CodeLayoutFingerprint`, with explicit legacy handling for journals from reviewed builds.

- [ ] **Step 1: Write failing journal tests**

Add tests that accept a matching new journal, reject changed client/layout fingerprints, preserve reviewed legacy journal recovery, and reject legacy journals for automatically discovered profiles.

- [ ] **Step 2: Run the suite and verify RED**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: new journal assertions fail before implementation.

- [ ] **Step 3: Implement journal binding**

Write both fingerprints before any owned write. During restore, create and validate the current context, require exact fingerprints for new journals, and allow missing fields only when the journal build resolves through an immutable reviewed profile.

- [ ] **Step 4: Run the suite and verify GREEN**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: all tests pass.

- [ ] **Step 5: Commit**

Commit message: `Bind recovery journals to runtime layouts`

### Task 5: Forced automatic discovery, documentation, and release verification

**Files:**
- Modify: `src/CameraProbe/Program.cs`
- Modify: `README.md`
- Modify: `docs/QUICKSTART.en.md`
- Modify: `docs/QUICKSTART.ru.md`
- Modify: `docs/verification.md`
- Modify: `Start-ConVars.cmd`
- Modify: portable packaging artifacts only under ignored `artifacts/releases/`
- Test: `tests/CameraProbe.Tests/Program.cs`

**Interfaces:**
- Consumes: all previous tasks.
- Produces: internal `--force-auto-discovery` diagnostic option, user-facing provenance output, updated portable package, and live equivalence evidence.

- [ ] **Step 1: Write failing command-line/provenance tests**

Add tests that the forced path bypasses reviewed-profile selection but still reuses a valid automatic cache, and that normal startup reports whether the active source is reviewed or automatic.

- [ ] **Step 2: Run the suite and verify RED**

Run: `dotnet run --project tests/CameraProbe.Tests -c Release`

Expected: forced-discovery behavior is absent.

- [ ] **Step 3: Implement diagnostics and documentation**

Keep the force option internal and diagnostic. Document normal automatic behavior, the bundled tool and license, cache location, offline operation, and fail-closed structural boundary in Russian and English.

- [ ] **Step 4: Run complete verification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1`

Run normal and forced-auto `snapshot`, `cvars`, `camera-restore`, and `movement-restore` against the current CS2 process. Compare all required schema fields, globals, ConVar RVA, and code-layout fingerprint between paths.

Publish a self-contained Windows x64 ZIP with the pinned tool, license, all reviewed profiles, no PDB, and a SHA-256 sidecar. Run the portable executable through both compatibility paths.

Expected: complete suite passes; paths are equivalent for current CS2; archive contains required files; live commands exit 0.

- [ ] **Step 5: Commit**

Commit message: `Document and package automatic discovery`

### Task 6: Whole-branch review and publication

**Files:**
- Review all files changed since the worktree base.
- Update only files required by Critical or Important review findings, with RED-to-GREEN tests.

**Interfaces:**
- Consumes: completed Tasks 1-5 and their ledger evidence.
- Produces: reviewed branch, final green verification, updated `main`, and a GitHub release asset.

- [ ] **Step 1: Create the review package and dispatch a fresh reviewer**

Use the plan/spec, full diff, Review Focus list, verification output, and ledger rulings.

- [ ] **Step 2: Re-grade findings and perform one fix pass**

Critical and Important findings receive a failing regression test, fix, and full green suite. Record Minor findings without expanding scope.

- [ ] **Step 3: Run final verification**

Run the full build, live normal/automatic commands, portable checks, `git diff --check`, and confirm the worktree is otherwise clean.

- [ ] **Step 4: Integrate and publish**

Fast-forward `main`, push to `origin/main`, publish a new GitHub release with ZIP and checksum, verify public assets, and launch the updated portable CameraProbe against the running game.

- [ ] **Step 5: Clean up**

Archive the managed worktree after preserving the commit and release references.
