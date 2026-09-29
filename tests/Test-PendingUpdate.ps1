$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$taskRoot = Join-Path $taskProject ('artifacts\update-test-' + [guid]::NewGuid().ToString('N'))
$taskLive = Join-Path $taskRoot 'artifacts\CameraProbe'
$taskPending = Join-Path $taskRoot 'artifacts\CameraProbe.update'
foreach ($taskPath in @($taskLive, (Join-Path $taskLive 'reference'), (Join-Path $taskLive 'captures'), $taskPending, (Join-Path $taskPending 'reference'))) {
    New-Item -ItemType Directory -Path $taskPath -Force | Out-Null
}
[IO.File]::WriteAllText((Join-Path $taskLive 'CameraProbe.dll'), 'old assembly')
[IO.File]::WriteAllText((Join-Path $taskLive 'reference\info.json'), 'old schema')
[IO.File]::WriteAllText((Join-Path $taskLive 'captures\cvars-session.json'), 'original journal')
[IO.File]::WriteAllText((Join-Path $taskPending 'CameraProbe.dll'), 'new assembly')
foreach ($taskFile in @('CameraProbe.deps.json','CameraProbe.runtimeconfig.json','reference\info.json')) {
    [IO.File]::WriteAllText((Join-Path $taskPending $taskFile), 'new metadata')
}
(Get-FileHash -LiteralPath (Join-Path $taskPending 'CameraProbe.dll')).Hash | Set-Content -LiteralPath (Join-Path $taskPending 'ready.sha256')
$taskLock = [IO.File]::Open((Join-Path $taskLive 'CameraProbe.dll'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $taskRejected = $false
    try { & (Join-Path $taskProject 'scripts\Apply-PendingUpdate.ps1') -ProjectRoot $taskRoot }
    catch { $taskRejected = $_.Exception.Message -like '*Ctrl+C*' }
    if (-not $taskRejected) { throw 'Locked assembly did not block update.' }
    if ([IO.File]::ReadAllText((Join-Path $taskLive 'reference\info.json')) -ne 'old schema') { throw 'Changed schema while old application was running.' }
} finally { $taskLock.Dispose() }
& (Join-Path $taskProject 'scripts\Apply-PendingUpdate.ps1') -ProjectRoot $taskRoot
if ([IO.File]::ReadAllText((Join-Path $taskLive 'CameraProbe.dll')) -ne 'new assembly') { throw 'Assembly was not updated.' }
if ([IO.File]::ReadAllText((Join-Path $taskLive 'reference\info.json')) -ne 'new metadata') { throw 'Reference files were not updated.' }
if ([IO.File]::ReadAllText((Join-Path $taskLive 'captures\cvars-session.json')) -ne 'original journal') { throw 'Session journal was modified.' }
if (Test-Path -LiteralPath (Join-Path $taskPending 'ready.sha256')) { throw 'Ready marker was not consumed.' }
& (Join-Path $taskProject 'scripts\Apply-PendingUpdate.ps1') -ProjectRoot $taskRoot
Write-Output 'PASS: locked application blocks update; restart installs it and preserves journal; repeat is a no-op.'
