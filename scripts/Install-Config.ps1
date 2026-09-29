param(
    [string]$GameCfgPath,
    [string]$KeysFile
)
$ErrorActionPreference = 'Stop'
if (-not $GameCfgPath) {
    $taskGame = @(Get-Process cs2 -ErrorAction Stop)
    if ($taskGame.Count -ne 1) { throw 'Exactly one running CS2 process is required, or specify -GameCfgPath.' }
    $taskGameRoot = Split-Path (Split-Path (Split-Path $taskGame[0].Path -Parent) -Parent) -Parent
    $GameCfgPath = Join-Path $taskGameRoot 'csgo\cfg'
}
if (-not (Test-Path -LiteralPath $GameCfgPath -PathType Container)) { throw "Game cfg folder not found: $GameCfgPath" }
if (-not $KeysFile) { throw 'Specify -KeysFile with your existing cs2_user_keys_0_slot0.vcfg to preserve F9/F10 bindings.' }
$taskKeys = Get-Content -LiteralPath $KeysFile -Raw
$taskRestore = @('// Restore persisted F9/F10 bindings saved by CameraProbe. Runtime-only changes are not captured.')
foreach ($taskKey in @('F9','F10')) {
    $taskPattern = '(?im)^\s*"' + $taskKey + '"\s*"((?:\\.|[^"\\])*)"\s*$'
    $taskMatches = [regex]::Matches($taskKeys, $taskPattern)
    if ($taskMatches.Count -gt 1) { throw "Ambiguous binding for $taskKey" }
    if ($taskMatches.Count -eq 1) { $taskRestore += 'bind "' + $taskKey + '" "' + $taskMatches[0].Groups[1].Value + '"' }
    else { $taskRestore += 'unbind "' + $taskKey + '"' }
}
$taskMainPath = Join-Path $GameCfgPath 'camera_probe.cfg'
$taskRestorePath = Join-Path $GameCfgPath 'camera_probe_restore.cfg'
if ((Test-Path -LiteralPath $taskMainPath) -or (Test-Path -LiteralPath $taskRestorePath)) {
    throw 'CameraProbe cfg already exists. Existing files were left unchanged.'
}
$taskMain = @(
    '// CameraProbe team input bindings. No camera recovery is implemented.',
    'bind "F9" "jointeam 2"',
    'bind "F10" "jointeam 3"',
    'echo "CameraProbe: F9=T, F10=CT. Restore with exec camera_probe_restore."'
)
$taskRestore += 'echo "CameraProbe: saved F9/F10 bindings restored."'
# CreateNew prevents overwriting a concurrently created configuration.
foreach ($taskEntry in @(@{Path=$taskRestorePath; Lines=$taskRestore}, @{Path=$taskMainPath; Lines=$taskMain})) {
    $taskStream = [System.IO.File]::Open($taskEntry.Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    $taskWriter = [System.IO.StreamWriter]::new($taskStream, [System.Text.UTF8Encoding]::new($false))
    try { foreach ($taskLine in $taskEntry.Lines) { $taskWriter.WriteLine($taskLine) } }
    finally { $taskWriter.Dispose() }
}
Write-Output "Created: $taskMainPath"
Write-Output "Created: $taskRestorePath"
Write-Output 'Enable in the CS2 console: exec camera_probe'
