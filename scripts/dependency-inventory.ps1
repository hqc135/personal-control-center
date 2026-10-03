# Offline inventory from locked packages and locally restored NuGet metadata. No online vulnerability claim.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force artifacts/evidence/B6 | Out-Null
$items = @{}
foreach ($lock in @(git ls-files '*packages.lock.json')) {
    $document = Get-Content -Raw -LiteralPath $lock | ConvertFrom-Json -AsHashtable
    foreach ($framework in $document.dependencies.Values) {
        foreach ($id in $framework.Keys) {
            $entry = $framework[$id]
            if ($entry.type -eq 'Project') { continue }
            $key = $id.ToLowerInvariant() + '/' + $entry.resolved
            if ($items.ContainsKey($key)) { continue }
            $directory = Join-Path '.tools/packages' $key
            $nuspec = Join-Path $directory ($id.ToLowerInvariant() + '.nuspec')
            if (!(Test-Path -LiteralPath $nuspec)) { throw ('Missing restored metadata: ' + $key) }
            [xml]$metadata = Get-Content -Raw -LiteralPath $nuspec
            $license = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
            $licenseUrl = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']")
            $items[$key] = [ordered]@{
                Id=$id; Version=$entry.resolved; Scope='build/test only'; ContentHash=$entry.contentHash;
                LicenseType=if($license){$license.GetAttribute('type')}else{'unspecified'};
                License=if($license){$license.InnerText}else{'unspecified'};
                LicenseUrl=if($licenseUrl){$licenseUrl.InnerText}else{''};
                MetadataSHA256=(Get-FileHash -LiteralPath $nuspec -Algorithm SHA256).Hash
            }
        }
    }
}
$projectionRoot = '.tools/packages/microsoft.windows.sdk.net.ref/10.0.22621.57'
[ordered]@{
    GeneratedAt=(Get-Date -Format o); RuntimeThirdPartyPackages=@([ordered]@{
        Id='Microsoft.Windows.SDK.NET.Ref'; Version='10.0.22621.57'; Scope='Windows projection runtime';
        LicenseUrl='https://aka.ms/WinSDKLicenseURL';
        ContentHash=(Get-Content -Raw ($projectionRoot + '/microsoft.windows.sdk.net.ref.10.0.22621.57.nupkg.sha512')).Trim();
        Assemblies=@('Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll');
        MetadataSHA256=(Get-FileHash ($projectionRoot + '/microsoft.windows.sdk.net.ref.nuspec') -Algorithm SHA256).Hash
    });
    VulnerabilityAudit='Not performed; offline inventory is not a vulnerability assessment.';
    Packages=@($items.Values | Sort-Object { $_.Id })
} | ConvertTo-Json -Depth 6 | Set-Content artifacts/evidence/B6/dependencies.json -Encoding utf8
Write-Output ('Locked packages inventoried: ' + $items.Count)
