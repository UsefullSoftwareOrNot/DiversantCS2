# Automatic runtime discovery for CameraProbe

## Goal

CameraProbe should continue working after routine CS2 updates without a source-code release for every engine build or `client.dll` fingerprint. It must discover the data schema, global addresses, ConVar interface, and guarded camera/movement code positions from the running local game. Any incomplete or ambiguous discovery must stop before process-memory writes or synthetic input.

Known fingerprinted profiles remain supported as a fast path and as evidence for regression tests. Automatic discovery is a compatibility path, not permission to reuse stale offsets.

## Supported update boundary

Automatic discovery covers updates where:

- cs2-dumper can still read the live SchemaSystem and required global patterns;
- the required class and field names still exist;
- the camera and movement branches still contain the verified semantic instruction shapes;
- the ConVar registry layout and the two target ConVar identities still pass the existing live validation;
- the local player, pawn, entity system, and game-rules invariants still hold.

If Valve changes those structures or semantics, CameraProbe reports the failed discovery stage and remains fail-closed. The design does not promise compatibility with arbitrary structural rewrites.

## Discovery sources

### Known-profile fast path

`reference/` remains the immutable set of reviewed profiles. An exact build plus `client.dll` SHA-256 match loads immediately. Legacy build 14186 keeps its explicit compatibility exception.

### Automatic profile path

The portable package includes the pinned `a2x/cs2-dumper 0.1.3` executable and its MIT license. CameraProbe verifies the dumper binary SHA-256 before executing it. It runs locally with JSON-only output, no network access, and no log file.

For an unknown build or fingerprint, CameraProbe creates a unique temporary directory below `captures/discovery/`, invokes the dumper against the one running `cs2.exe`, and enforces a bounded timeout. It accepts output only when:

- `info.json`, `client_dll.json`, `offsets.json`, and `interfaces.json` all exist and parse;
- `info.json` build equals the build independently discovered from loaded `engine2.dll`;
- the CS2 process ID and start time did not change during discovery;
- the file-backed `client.dll` SHA-256 still matches the loaded module;
- all required globals, classes, fields, and `VEngineCvar007` are present exactly once;
- every numeric RVA and field offset is in a valid range.

The completed profile is published atomically to `captures/discovery/profiles/<build>/<client-sha256>/`. Partial output is never treated as a profile. A cached automatic profile is revalidated against the current process and module hash on every use.

## Runtime code discovery

Static `PlayerCodeLayout.ForBuild` remains for regression evidence but is no longer the only compatibility gate. A new `PlayerCodeLocator` scans executable sections copied from the loaded `client.dll`.

It locates four required blocks:

1. camera entry and virtual-call guard;
2. load of `m_flDeathTime`;
3. the camera floating-point comparison and guarded return path;
4. the movement health comparison using `m_iHealth`.

Patterns mask only relocation-dependent call and RIP-relative displacements. Opcodes, branch directions, object-field displacements, and surrounding control-flow bytes remain fixed. Each pattern must have exactly one match. The decoded field displacement must equal the automatically discovered schema field offset. Relative targets must stay inside the expected loaded module. The discovered bytes and RVAs are retained in the active compatibility context and rechecked immediately before each guarded write.

Known layouts and automatically located layouts use the same verification path. This prevents a static build entry from bypassing runtime validation.

## ConVar discovery

The `VEngineCvar007` RVA is read from the accepted `interfaces.json` rather than selected solely by engine build. Existing `ConVarReader` validation remains authoritative: module bounds, list bounds, node identity, vtable location, names, types, uniqueness, and restriction flags are checked before writes and again during restoration.

Known profiles may supply the same interface RVA through their stored `interfaces.json`. `ConVarLayout.ForBuild` becomes a legacy fallback for existing recovery journals only.

## Compatibility context

A new immutable `CompatibilityContext` owns:

- engine build and process identity;
- client module SHA-256 and image metadata;
- parsed schema field offsets;
- parsed/discovered global RVAs;
- the ConVar interface RVA;
- the dynamically located camera and movement code layout;
- provenance: `reviewed-profile` or `automatic-profile`.

`GameReader` and `ConVarReader` consume this context instead of independently selecting build tables. Readers created by background recovery workers must share equivalent context and confirm process identity.

## Startup and failure flow

1. Open the single CS2 process and validate loaded PE images.
2. Discover the engine build and hash the same validated `client.dll` file handle.
3. Try an exact reviewed profile.
4. If unavailable, validate a cached automatic profile or run the pinned local dumper.
5. Parse required schema, globals, and interface offsets.
6. Locate and validate the four code blocks in loaded memory.
7. Capture a read-only snapshot and validate entity/pawn/game-rules invariants.
8. Only after all prior stages succeed may the existing ConVar, camera, movement, and F6 flows start.

Errors identify the failed stage and preserve the original cause. Temporary discovery output is retained for diagnosis but is clearly marked incomplete. No fallback chooses the “closest” build or newest schema.

## Journals and restoration

New camera and movement journals record the client SHA-256 and a compact hash of the discovered code layout in addition to build and process identity. Restoration requires the same process start time, module hash, schema offsets, and code-layout hash. Existing journals from known supported builds remain readable through their reviewed profiles.

ConVar journals continue to rediscover live entries by identity. Automatic profiles cannot weaken the current restoration rules.

## Packaging and provenance

The repository and portable ZIP include:

- the pinned dumper executable;
- the dumper MIT license and source/release metadata;
- the expected dumper SHA-256 in source code;
- documentation explaining automatic discovery and its fail-closed boundary.

The application performs no download. Updating the bundled dumper requires a normal reviewed repository change.

## Tests

Unit tests cover:

- exact and wildcard pattern matching, uniqueness, bounds, and relative-target decoding;
- schema/profile acceptance and rejection for missing, duplicate, malformed, or out-of-range values;
- cache publication and rejection of incomplete profiles;
- dumper hash verification, timeout, nonzero exit, process restart, and changed module fingerprint;
- dynamic ConVar interface selection;
- compatibility-context equality across recovery workers;
- journal compatibility and rejection after module/layout changes;
- fail-closed behavior before every write path.

Integration verification uses the currently running CS2 twice: once through the reviewed profile and once through a forced automatic-discovery directory. Both paths must produce equivalent required offsets, a warning-free snapshot, a validated ConVar registry, and successful camera/movement restore-only code checks. The full build, portable package, archive contents, and checksum are verified before publication.

## Non-goals

- guessing after ambiguous signatures;
- patching executable game code;
- downloading offsets or binaries at runtime;
- bypassing validation because a build number is close to a known build;
- guaranteeing compatibility after class names, registry layout, or camera/movement semantics change.
