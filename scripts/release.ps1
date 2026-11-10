param([string]$IsccPath = $env:ISCC_PATH)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SnipLite\SnipLite.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$releaseDir = Join-Path $projectRoot "artifacts\release\$version"
if (-not $IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup and provide -IsccPath or ISCC_PATH.' }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

& (Join-Path $PSScriptRoot 'publish.ps1') -SelfContained
$portableDir = Join-Path $projectRoot 'artifacts\publish\win-x64\self-contained'
$portableZip = Join-Path $releaseDir "SnipLite-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force

& (Join-Path $PSScriptRoot 'publish.ps1') -SingleFile
$singleExe = Join-Path $releaseDir "SnipLite-$version-win-x64-single.exe"
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\publish\win-x64\single-file\SnipLite.exe') -Destination $singleExe -Force

& $IsccPath '/Qp' "/DAppVersion=$version" "/DProjectRoot=$projectRoot" "/DPublishDir=$portableDir" "/DReleaseDir=$releaseDir" (Join-Path $projectRoot 'packaging\windows\SnipLite.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE." }

$assets = @($singleExe, (Join-Path $releaseDir "SnipLite-$version-win-x64-setup.exe"), $portableZip)
$hashes = $assets | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $assets | Select-Object Name, Length
Write-Host "Release assets: $releaseDir"
