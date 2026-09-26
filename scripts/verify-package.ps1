param([ValidateSet('framework-dependent','self-contained')][string]$Mode = 'framework-dependent')
$ErrorActionPreference = 'Stop'
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
Set-Location (Split-Path $PSScriptRoot -Parent)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$workspace=(Get-Location).Path
$commit=git rev-parse HEAD
if (@(git status --porcelain).Count) { throw 'Source tree changed after packaging.' }
$sourceManifest=Get-Content -Raw artifacts/evidence/B5/source-manifest.json | ConvertFrom-Json
$source=[IO.Compression.ZipFile]::OpenRead((Join-Path $workspace 'artifacts/PersonalControlCenter-B5-source.zip'))
function Read-EntryHash($entry) {
    $stream=$entry.Open()
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
}
try {
    foreach($item in $sourceManifest) {
        $entry=$source.GetEntry($item.Path.Replace('\','/'))
        if(!$entry) { throw ('Missing source entry: '+$item.Path) }
        if((Read-EntryHash $entry) -ne $item.SHA256 -or (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash -ne $item.SHA256) { throw ('Source hash mismatch: '+$item.Path) }
    }
} finally { $source.Dispose() }
$zipPath='artifacts/PersonalControlCenter-B5-CANDIDATE-'+$Mode+'.zip'
$release=[IO.Compression.ZipFile]::OpenRead((Join-Path $workspace $zipPath))
try {
    $reader=[IO.StreamReader]::new($release.GetEntry('RELEASE.json').Open())
    try { $manifest=$reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if($manifest.Commit -ne $commit -or $manifest.Mode -ne $Mode) { throw 'Release identity mismatch' }
    if($release.Entries.Count -ne $manifest.Files.Count+1) { throw 'Unexpected release entry count' }
    foreach($item in $manifest.Files) {
        $entry=$release.GetEntry($item.Path.Replace('\','/'))
        if(!$entry -or (Read-EntryHash $entry) -ne $item.SHA256) { throw ('Release hash mismatch: '+$item.Path) }
        if($item.Path -match '(^|[\\/])(config.json|state.json|trusted-shortcuts.json|lifecycle.log)$|\.pdb$|UiAcceptance|ReadOnlyProbe') { throw 'Private or test payload in release' }
    }
    $reader=[IO.StreamReader]::new($release.GetEntry('PersonalControlCenter.runtimeconfig.json').Open())
    try { $runtime=$reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if($Mode -eq 'framework-dependent' -and !$runtime.runtimeOptions.frameworks) { throw 'Framework-dependent runtime contract missing' }
    if($Mode -eq 'self-contained' -and (!$release.GetEntry('coreclr.dll') -or !$release.GetEntry('PresentationFramework.dll'))) { throw 'Desktop runtime missing' }
    $build=Get-Content -Raw ('artifacts/evidence/B5/build-'+$Mode+'.json') | ConvertFrom-Json
    foreach($item in $build.Files) {
        if($item.Path -like '*.pdb') { continue }
        if((Read-EntryHash $release.GetEntry($item.Path.Replace('\','/'))) -ne $item.SHA256) { throw 'Package differs from tested build' }
    }
} finally { $release.Dispose() }
[xml]$trx=Get-Content -Raw artifacts/evidence/B5/unit-tests.trx
$counters=$trx.TestRun.ResultSummary.Counters
if([int]$counters.failed -ne 0 -or [int]$counters.passed -eq 0) { throw 'Test evidence missing or failed' }
[ordered]@{
    Commit=$commit;Mode=$Mode;SourceFilesChecked=$sourceManifest.Count;SourceArchiveMatches=$true;ReleasePayloadMatches=$true;
    TestsPassed=[int]$counters.passed;TestsFailed=[int]$counters.failed;
    RelatedProcessesCurrentlyRunning=@(Get-Process -Name PersonalControlCenter,PersonalControlCenter.Demo,UiAcceptance,ReadOnlyProbe -ErrorAction SilentlyContinue).Count
} | ConvertTo-Json | Set-Content artifacts/evidence/B5/delivery-check.json -Encoding utf8
Get-Content artifacts/evidence/B5/delivery-check.json
