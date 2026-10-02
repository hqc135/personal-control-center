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
New-Item -ItemType Directory -Force artifacts/evidence/B6 | Out-Null
if ($SelfContained) {
    foreach ($id in @('microsoft.windowsdesktop.app.runtime.win-x64','microsoft.netcore.app.runtime.win-x64')) {
        $path = '.tools/runtime-feed/' + $id + '.10.0.12.nupkg'
        if (!(Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Runtime feed missing; run scripts/restore-runtime.ps1 first.' }
    }
}
if (!$Offline) {
    & $dotnet restore ControlCenter.slnx --locked-mode
    if ($LASTEXITCODE) { throw 'Restore failed' }
}
& $dotnet build ControlCenter.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'Build failed' }
& $dotnet test tests/ControlCenter.Tests -c Release --no-build --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts/evidence/B6
if ($LASTEXITCODE) { throw 'Tests failed' }
if ($Publish) {
    $mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
    $output = 'artifacts/B6-CANDIDATE-' + $mode
    if ($SelfContained) {
        & $dotnet publish src/ControlCenter.App -c Release -r win-x64 --self-contained true --source .tools/runtime-feed --source .tools/packages -p:NuGetAudit=false -p:RestorePackagesWithLockFile=true -p:NuGetLockFilePath=obj/self-contained.packages.lock.json -p:RuntimeFrameworkVersion=10.0.12 -p:PublishTrimmed=false -p:PublishReadyToRun=false -m:1 -p:UseSharedCompilation=false -o $output
    } else {
        & $dotnet publish src/ControlCenter.App -c Release --no-restore --no-build --self-contained false -o $output
    }
    if ($LASTEXITCODE) { throw 'Publish failed' }
    [ordered]@{
        Commit=(git rev-parse HEAD); Dirty=(@(git status --porcelain).Count -ne 0); Mode=$mode; SDK=(& $dotnet --version);
        Files=@(Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
            [ordered]@{Path=[IO.Path]::GetRelativePath((Join-Path (Get-Location) $output), $_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        })
    } | ConvertTo-Json -Depth 5 | Set-Content ('artifacts/evidence/B6/build-' + $mode + '.json') -Encoding utf8
}

