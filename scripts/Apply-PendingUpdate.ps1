param([Parameter(Mandatory=$true)][string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
$taskPending = Join-Path $ProjectRoot 'artifacts\CameraProbe.update'
$taskReady = Join-Path $taskPending 'ready.sha256'
if (-not (Test-Path -LiteralPath $taskReady)) { return }
$taskSource = Join-Path $taskPending 'CameraProbe.dll'
$taskDestination = Join-Path $ProjectRoot 'artifacts\CameraProbe'
$taskExpected = (Get-Content -LiteralPath $taskReady -Raw).Trim()
if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -ne $taskExpected) { throw 'Pending update checksum mismatch.' }
try {
    # Copy DLL first: a running old instance must block installation before any references change.
    Copy-Item -LiteralPath $taskSource -Destination (Join-Path $taskDestination 'CameraProbe.dll') -Force
} catch { throw 'Close the old CameraProbe window with Ctrl+C, then start this launcher again. CS2 can remain open.' }
foreach ($taskName in @('CameraProbe.deps.json', 'CameraProbe.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $taskPending $taskName) -Destination (Join-Path $taskDestination $taskName) -Force
}
Copy-Item -LiteralPath (Join-Path $taskPending 'reference') -Destination $taskDestination -Recurse -Force
Move-Item -LiteralPath $taskReady -Destination (Join-Path $taskPending 'applied.sha256') -Force
Write-Output 'CameraProbe update installed. Session journals and captures were preserved.'
