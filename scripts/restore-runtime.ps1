param([string]$Version = '10.0.12')
$ErrorActionPreference = 'Stop'
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
Set-Location (Split-Path $PSScriptRoot -Parent)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid runtime version' }
New-Item -ItemType Directory -Force .tools/runtime-feed,artifacts/evidence/B6 | Out-Null
$receipts = @()
foreach ($id in @('microsoft.windowsdesktop.app.runtime.win-x64','microsoft.netcore.app.runtime.win-x64')) {
    $file = Join-Path (Get-Location) ('.tools/runtime-feed/' + $id + '.' + $Version + '.nupkg')
    $header = $file + '.headers'
    $url = 'https://api.nuget.org/v3-flatcontainer/' + $id + '/' + $Version + '/' + $id + '.' + $Version + '.nupkg'
    & curl.exe --fail --silent --show-error --retry 2 --retry-all-errors --retry-delay 2 --limit-rate 512K --connect-timeout 10 --max-time 240 --dump-header $header --output $file $url
    if ($LASTEXITCODE) { throw ('Runtime package download failed: ' + $id) }
    $hashHeaders = @(Get-Content -LiteralPath $header | Where-Object { $_ -match '^x-ms-meta-SHA512:' })
    if (!$hashHeaders.Count) { throw 'Official SHA512 response header missing' }
    $declared = $hashHeaders[-1].Split(':',2)[1].Trim()
    $stream = [IO.File]::OpenRead($file)
    try { $actual = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData($stream)) } finally { $stream.Dispose() }
    if ($actual -ne $declared) { throw 'Runtime package SHA512 mismatch' }
    $receipts += [pscustomobject]@{ Package=$id; Version=$Version; Source=$url; Bytes=(Get-Item $file).Length; SHA512=$actual; Verified=$true }
    $receipts | ConvertTo-Json | Set-Content artifacts/evidence/B6/runtime-downloads.json -Encoding utf8
    Write-Output ($id + ' SHA512 verified')
}
