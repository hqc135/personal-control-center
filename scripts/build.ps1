param([switch]$Publish, [switch]$SelfContained, [switch]$Offline)
$ErrorActionPreference = 'Stop'
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/packages'
$dotnet = Join-Path (Get-Location) '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { throw 'Project-local SDK is missing; see global.json.' }
New-Item -ItemType Directory -Force artifacts/evidence/B4 | Out-Null
if ($Offline -and $SelfContained) { throw 'Self-contained publish requires restoring runtime packs; do not combine with Offline.' }
if (!$Offline) {
    & $dotnet restore ControlCenter.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed' }
}
& $dotnet build ControlCenter.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'Build failed' }
& $dotnet test tests/ControlCenter.Tests -c Release --no-build --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts/evidence/B4
if ($LASTEXITCODE) { throw 'Tests failed' }
if ($Publish) {
    if ($SelfContained) {
        & $dotnet publish src/ControlCenter.App -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -m:1 -p:UseSharedCompilation=false -o artifacts/B4-CANDIDATE-self-contained
    } else {
        & $dotnet publish src/ControlCenter.App -c Release --no-restore --no-build --self-contained false -o artifacts/B4-CANDIDATE-framework-dependent
    }
    if ($LASTEXITCODE) { throw 'Publish failed' }
}

