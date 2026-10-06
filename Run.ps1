param([switch]$EnableSwitching, [ValidateRange(30,1000)][int]$DelayMs = 75, [switch]$Snapshot,
    [ValidateSet('Inspect','Unlock','Restore')][string]$ConVars, [switch]$Play,
    [switch]$ForceAutoDiscovery)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'scripts\Apply-PendingUpdate.ps1') -ProjectRoot $PSScriptRoot
$taskAssembly = Join-Path $PSScriptRoot 'artifacts\CameraProbe\CameraProbe.dll'
if (-not (Test-Path -LiteralPath $taskAssembly)) { throw 'Run scripts\Build.ps1 first.' }
# Invoke the SDK host explicitly: this computer also has an older DOTNET_ROOT on W:.
$taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source
$taskCompatibilityArgs = if ($ForceAutoDiscovery) { @('--force-auto-discovery') } else { @() }
if ($Play) {
    if ($ConVars -or $Snapshot) { throw 'Play cannot be combined with ConVars or Snapshot.' }
    if ($EnableSwitching) { & $taskDotnet $taskAssembly play --enable-switching --delay-ms $DelayMs @taskCompatibilityArgs }
    else { & $taskDotnet $taskAssembly play --delay-ms $DelayMs @taskCompatibilityArgs }
}
elseif ($ConVars) {
    if ($Snapshot -or $EnableSwitching) { throw 'ConVars is a separate mode; do not combine it with Snapshot or EnableSwitching.' }
    $taskMode = @{Inspect='cvars'; Unlock='cvars-unlock'; Restore='cvars-restore'}[$ConVars]
    & $taskDotnet $taskAssembly $taskMode @taskCompatibilityArgs
}
elseif ($Snapshot) { & $taskDotnet $taskAssembly snapshot @taskCompatibilityArgs }
elseif ($EnableSwitching) { & $taskDotnet $taskAssembly play --enable-switching --delay-ms $DelayMs @taskCompatibilityArgs }
else { & $taskDotnet $taskAssembly watch @taskCompatibilityArgs }
exit $LASTEXITCODE
