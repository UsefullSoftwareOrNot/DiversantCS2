$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore tests/CameraProbe.Tests --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet run --project tests/CameraProbe.Tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $taskRoot 'tests\Test-ConfigInstaller.ps1')
    & (Join-Path $taskRoot 'tests\Test-PendingUpdate.ps1')
    dotnet publish src/CameraProbe -c Release --no-restore --self-contained false -o artifacts/CameraProbe
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $taskPendingMarker = Join-Path $taskRoot 'artifacts\CameraProbe.update\ready.sha256'
    if (Test-Path -LiteralPath $taskPendingMarker) {
        Move-Item -LiteralPath $taskPendingMarker -Destination (Join-Path $taskRoot 'artifacts\CameraProbe.update\applied.sha256') -Force
    }
} finally { Pop-Location }
