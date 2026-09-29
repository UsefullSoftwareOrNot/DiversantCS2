$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$taskCase = Join-Path $taskProject ('artifacts\installer-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskCase -Force | Out-Null
$taskKeysPath = Join-Path $taskCase 'keys.vcfg'
[IO.File]::WriteAllText($taskKeysPath, @'
"bindings"
{
    "F9" "say \"test\"; slot1"
}
'@)
& (Join-Path $taskProject 'scripts\Install-Config.ps1') -GameCfgPath $taskCase -KeysFile $taskKeysPath
$taskRestore = Get-Content (Join-Path $taskCase 'camera_probe_restore.cfg') -Raw
if (-not $taskRestore.Contains('bind "F9" "say \"test\"; slot1"')) { throw 'F9 binding was not preserved exactly.' }
if (-not $taskRestore.Contains('unbind "F10"')) { throw 'Previously unbound F10 was not restored.' }
$taskOriginalHash = (Get-FileHash (Join-Path $taskCase 'camera_probe.cfg')).Hash
$taskRejected = $false
try { & (Join-Path $taskProject 'scripts\Install-Config.ps1') -GameCfgPath $taskCase -KeysFile $taskKeysPath }
catch { $taskRejected = $_.Exception.Message -like '*already exists*' }
if (-not $taskRejected) { throw 'Installer did not reject overwriting an existing cfg.' }
if ((Get-FileHash (Join-Path $taskCase 'camera_probe.cfg')).Hash -ne $taskOriginalHash) { throw 'Existing cfg was changed.' }
Write-Output 'PASS: binding escaping, absent binding restoration, and existing-file protection.'
