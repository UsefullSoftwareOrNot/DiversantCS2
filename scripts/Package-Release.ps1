param([string]$Name = 'DiversantCS2-win-x64-auto-discovery')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskReleaseRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts\releases'))
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskReleaseRoot ($Name + '-stage')))
if (-not $taskStage.StartsWith($taskReleaseRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release staging path.' }
$taskZip = Join-Path $taskReleaseRoot ($Name + '.zip')
$taskChecksum = $taskZip + '.sha256'

New-Item -ItemType Directory -Path $taskReleaseRoot -Force | Out-Null
if (Test-Path -LiteralPath $taskStage) { Remove-Item -LiteralPath $taskStage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $taskStage 'artifacts\CameraProbe') -Force | Out-Null

dotnet publish (Join-Path $taskRoot 'src\CameraProbe') -c Release -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $taskStage 'artifacts\CameraProbe')
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

Copy-Item (Join-Path $taskRoot 'Run.ps1'), (Join-Path $taskRoot 'Start-ConVars.cmd'),
    (Join-Path $taskRoot 'Start-CameraProbe.cmd'), (Join-Path $taskRoot 'README.md') -Destination $taskStage
New-Item -ItemType Directory -Path (Join-Path $taskStage 'scripts') -Force | Out-Null
Copy-Item (Join-Path $taskRoot 'scripts\Apply-PendingUpdate.ps1') -Destination (Join-Path $taskStage 'scripts')
New-Item -ItemType Directory -Path (Join-Path $taskStage 'docs') -Force | Out-Null
Copy-Item (Join-Path $taskRoot 'docs\QUICKSTART.en.md'), (Join-Path $taskRoot 'docs\QUICKSTART.ru.md') `
    -Destination (Join-Path $taskStage 'docs')

Get-ChildItem $taskStage -Filter *.pdb -Recurse | Remove-Item -Force
if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip -Force }
Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskZip -CompressionLevel Optimal
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $taskChecksum -Encoding ascii -NoNewline -Value "$taskHash  $([IO.Path]::GetFileName($taskZip))"
Remove-Item -LiteralPath $taskStage -Recurse -Force
Write-Output $taskZip
Write-Output $taskChecksum
