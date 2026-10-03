param([ValidateSet('framework-dependent','self-contained')][string]$Mode = 'framework-dependent')
# Packages a clean Git commit and matching build; never starts the application.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
$workspace = (Get-Location).Path
$commit = git rev-parse HEAD
if ($LASTEXITCODE -or @(git status --porcelain).Count) { throw 'Commit all source changes before packaging.' }
$published = Join-Path $workspace ('artifacts/B6-CANDIDATE-' + $Mode)
$receipt = Get-Content -Raw ('artifacts/evidence/B6/build-' + $Mode + '.json') | ConvertFrom-Json
if ($receipt.Dirty -or $receipt.Commit -ne $commit -or $receipt.Mode -ne $Mode) { throw 'Build does not match clean HEAD; rebuild.' }
foreach ($entry in $receipt.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $published $entry.Path) -Algorithm SHA256).Hash -ne $entry.SHA256) { throw ('Published output changed: ' + $entry.Path) }
}
if (!(Test-Path (Join-Path $published 'PersonalControlCenter.exe'))) { throw 'Published executable missing.' }
if ($Mode -eq 'self-contained' -and !(Test-Path (Join-Path $published 'coreclr.dll'))) { throw 'Self-contained runtime missing.' }
$stage = Join-Path $workspace ('artifacts/staging-' + [Guid]::NewGuid().ToString('N'))
$release = Join-Path $stage 'release'
$source = Join-Path $stage 'source'
New-Item -ItemType Directory -Force $release,$source | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $published -File -Recurse) {
    if ($file.Extension -eq '.pdb') { continue }
    $destination = Join-Path $release ([IO.Path]::GetRelativePath($published, $file.FullName))
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Copy-Item -LiteralPath 'docs/PACKAGE_README.md' -Destination (Join-Path $release 'README.md')
Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md','docs/config.schema.json','docs/CONFIGURATION_GUIDE.md','docs/AUDIO_COMPATIBILITY.md','docs/RELEASE_CHECKLIST.md','docs/USER_GUIDE.md','docs/MAINTENANCE.md','docs/THEMES.md','docs/theme.example.css' -Destination $release
Copy-Item -LiteralPath 'artifacts/evidence/B6/dependencies.json' -Destination (Join-Path $release 'DEPENDENCIES.json')
Copy-Item -LiteralPath '.tools/packages/microsoft.windows.sdk.net.ref/10.0.22621.57/microsoft.windows.sdk.net.ref.nuspec' -Destination (Join-Path $release 'WINDOWS-SDK-NET-METADATA.xml')
if ($Mode -eq 'self-contained') {
    $licenses = Join-Path $release 'runtime-notices'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    foreach ($id in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $root = Join-Path '.tools/packages' ($id + '/10.0.12')
        $notices = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Name -imatch '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES(\.TXT)?)$' })
        if (!($notices | Where-Object { $_.Name -imatch '^LICENSE(\.TXT)?$' })) { throw ('Missing runtime license: ' + $id) }
        foreach ($notice in $notices) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses ($id + '-' + $notice.Name))
        }

    }
}
'{"schemaVersion":1,"hotkey":null,"appearance":{"theme":"system","fontFamily":"PingFang SC","motion":"subtle","material":"solid"},"proxy":{"host":"127.0.0.1","port":7897,"testUrl":null,"shortcutId":null},"modules":["audio","power","awake","proxy","shortcuts"],"shortcuts":[{"id":"downloads","label":"下载","kind":"knownFolder","target":"Downloads"}]}' | Set-Content -LiteralPath (Join-Path $release 'config.example.json') -Encoding utf8
$manifest = @(foreach ($relative in @(git -c core.quotepath=false ls-files)) {
    if ($relative -match '^(\.tools|artifacts|\.git)/|(^|/)(bin|obj)/') { throw 'Non-source file tracked by Git.' }
    $destination = Join-Path $source $relative
    New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
    Copy-Item -LiteralPath $relative -Destination $destination
    [PSCustomObject]@{Path=$relative; SHA256=(Get-FileHash -LiteralPath $relative -Algorithm SHA256).Hash}
})
$manifest | ConvertTo-Json | Set-Content artifacts/evidence/B6/source-manifest.json -Encoding utf8
$commit | Set-Content -LiteralPath (Join-Path $source 'SOURCE_COMMIT.txt') -Encoding ascii
$payload = @(Get-ChildItem -LiteralPath $release -File -Recurse | ForEach-Object {
    [ordered]@{Path=[IO.Path]::GetRelativePath($release,$_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{Version='0.8.0';Commit=$commit;Mode=$Mode;Acceptance='Automated, fake-service UI and real-machine read-only acceptance; hardware writes untested';Files=$payload} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $release 'RELEASE.json') -Encoding utf8
$zip = 'artifacts/PersonalControlCenter-B6-CANDIDATE-' + $Mode + '.zip'
Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -Force
Compress-Archive -Path (Join-Path $source '*') -DestinationPath artifacts/PersonalControlCenter-B6-source.zip -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $zip,'artifacts/PersonalControlCenter-B6-source.zip','artifacts/evidence/B6/source-manifest.json' |
    ForEach-Object { $_.Hash + '  ' + (Split-Path $_.Path -Leaf) } | Set-Content ('artifacts/B6-' + $Mode + '-SHA256.txt') -Encoding utf8
Get-Content ('artifacts/B6-' + $Mode + '-SHA256.txt')
