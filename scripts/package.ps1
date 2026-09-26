# Packages existing build outputs only; does not launch the application or contact the network.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$workspace = (Get-Location).Path
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$published = Join-Path $workspace 'artifacts/B4-CANDIDATE-framework-dependent'
if (!(Test-Path (Join-Path $published 'PersonalControlCenter.exe'))) { throw 'Publish the candidate before packaging.' }
$stage = Join-Path $workspace ('artifacts/staging-' + [Guid]::NewGuid().ToString('N'))
$release = Join-Path $stage 'release'
$source = Join-Path $stage 'source'
New-Item -ItemType Directory -Force $release,$source | Out-Null
Get-ChildItem -LiteralPath $published -File | Where-Object { $_.Extension -in '.exe','.dll','.json' } | Copy-Item -Destination $release
Copy-Item -LiteralPath (Join-Path $published 'Assets') -Destination $release -Recurse
Copy-Item -LiteralPath 'docs/PACKAGE_README.md' -Destination (Join-Path $release 'README.md')
Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md' -Destination $release
Copy-Item -LiteralPath 'docs/config.schema.json','docs/CONFIGURATION_GUIDE.md','docs/AUDIO_COMPATIBILITY.md' -Destination $release
'{"schemaVersion":1,"hotkey":null,"appearance":{"theme":"system","fontFamily":"PingFang SC","motion":"subtle","material":"solid"},"proxy":{"host":"127.0.0.1","port":7897,"testUrl":null,"shortcutId":null},"modules":["audio","power","awake","proxy","shortcuts"],"shortcuts":[{"id":"downloads","label":"下载","kind":"knownFolder","target":"Downloads"}]}' | Set-Content -LiteralPath (Join-Path $release 'config.example.json') -Encoding utf8
$files = @(rg --files --hidden -g '!.git/**' -g '!.tools/**' -g '!artifacts/**' -g '!**/bin/**' -g '!**/obj/**' | Sort-Object)
$manifest = @(
    foreach ($relative in $files) {
        $destination = Join-Path $source $relative
        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $relative -Destination $destination
        [PSCustomObject]@{Path=$relative; SHA256=(Get-FileHash -LiteralPath $relative -Algorithm SHA256).Hash}
    }
)
$manifest | ConvertTo-Json | Set-Content 'artifacts/evidence/B4/source-manifest.json' -Encoding utf8
Compress-Archive -Path (Join-Path $release '*') -DestinationPath 'artifacts/PersonalControlCenter-B4-CANDIDATE-framework-dependent.zip' -Force
Compress-Archive -Path (Join-Path $source '*') -DestinationPath 'artifacts/PersonalControlCenter-B4-source.zip' -Force
Get-FileHash -Algorithm SHA256 -LiteralPath 'artifacts/PersonalControlCenter-B4-CANDIDATE-framework-dependent.zip','artifacts/PersonalControlCenter-B4-source.zip','artifacts/evidence/B4/source-manifest.json' |
    ForEach-Object { $_.Hash + '  ' + (Split-Path $_.Path -Leaf) } | Set-Content 'artifacts/B4-SHA256.txt' -Encoding utf8
Get-Content 'artifacts/B4-SHA256.txt'

