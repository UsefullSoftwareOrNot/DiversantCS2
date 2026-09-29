param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskLogs = Join-Path $taskRoot 'artifacts\MovementExperiment\captures'
New-Item -ItemType Directory -Path $taskLogs -Force | Out-Null
$taskLog = Join-Path $taskLogs ('launch-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.txt')
Start-Transcript -LiteralPath $taskLog | Out-Null
$taskExit = 1
try {
    Write-Output "Launcher log: $taskLog"
    $taskAssembly = Join-Path $taskRoot 'artifacts\MovementExperiment\CameraProbe.dll'
    if (-not (Test-Path -LiteralPath $taskAssembly)) { throw 'Movement experiment build is missing.' }
    $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $taskMode = if ($Restore) { 'movement-restore' } else { 'movement-experiment' }
    & $taskDotnet $taskAssembly $taskMode
    $taskExit = $LASTEXITCODE
    Write-Output "Movement experiment exit code: $taskExit"
}
catch { Write-Output "Movement launcher error: $_" }
finally { Stop-Transcript | Out-Null }
exit $taskExit
