# Camera capture findings — 2026-09-29

Sources: `artifacts/CameraProbe/captures/camera-20260929-013619-345-e4554ca66eec4fca9b5e7b05402b620a.jsonl` and `camera-20260929-013627-894-f55fabf8f32d4d268bcb6535e4821a0e.jsonl`.

Both F7 recordings completed with 130 samples and no reported read warnings, using engine build 14186. These are client memory observations, not authoritative server telemetry.

## Transition captured in the first recording

| Elapsed ms | Team | Freeze | Health | PawnIsAlive | LifeState | LastCameraTime |
| --- | --- | --- | --- | --- | --- | --- |
| 81 | 2 | false | 100 | false | 2 | 0 |
| 1874 | 2 | true | 100 | true | 0 | 70.439415 |
| 5523 | 3 | true | 100 | true | 0 | 74.0625 |
| 6207 | 2 | true | 100 | false | 2 | 74.78125 |
| 6270 | 2 | true | 100 | false | 2 | 0 |
| 6891 | 2 | false | 100 | false | 2 | 0 |

The controller's active pawn handle remained 28016981. At 6270 ms rendered yaw settled at 44.531242 degrees while input and pawn yaw continued changing. The transition coincided with returning from team 3 to team 2. Correlation does not establish which internal camera branch caused the frozen direction. LastCameraTime resetting to zero is an observation; it does not prove that all camera processing stopped.

## Second recording

All 130 samples retained health 100, PawnIsAlive false and LifeState 2. Input and pawn yaw ranged from -166.90454 to -23.49867 degrees. Rendered pitch/yaw remained [5.5624976, 44.531242]. LastCameraOrigin changed from [-576, 2252, 24.031252] to [-544.09827, 2108.0056, 22.031248]; this does not establish server-accepted movement.

This rules out absent mouse-angle input in this recording. Full health and client life-state fields disagree with the assumption of an ordinary live pawn. It does not prove that the server considers the player dead, or explain the reported teammate-damage kicks.

## Limits and next investigation

Both recordings read spec_freeze_time=3 and mat_fullbright=0 at their start, with the two restriction bits removed. The logger reads ConVars once at capture start; it cannot establish their values for every subsequent sample. Neither recording contains an F6 command-submission trace. Thus these files do not test the intended 1000/1 settings and do not establish whether F6 succeeded or was used.

Observer services are absent on the resolved player pawn, so observer-mode values are null. Null does not mean observer mode zero. No camera fix or life-state write was applied based on these captures. A repair requires identifying the camera update path used in this inconsistent state; blindly changing life-state flags would not establish correct camera or server movement behavior.
