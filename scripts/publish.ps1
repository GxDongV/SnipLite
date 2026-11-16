param([switch]$SelfContained, [switch]$SingleFile, [string]$OutputRoot)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectRoot 'src\SnipLite\SnipLite.csproj'
$flavor = if ($SingleFile) { 'single-file' } elseif ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$buildRoot = if ($OutputRoot) { [IO.Path]::GetFullPath($OutputRoot) } else { Join-Path $projectRoot 'artifacts' }
$output = Join-Path $buildRoot "publish\win-x64\$flavor"
$contained = if ($SelfContained -or $SingleFile) { 'true' } else { 'false' }
$single = if ($SingleFile) { 'true' } else { 'false' }

# dotnet publish does not remove obsolete dependencies from an existing directory.
# Only clear this script's fixed publish leaf, never the supplied output root.
$expectedParent = [IO.Path]::GetFullPath((Join-Path $buildRoot 'publish\win-x64'))
if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($output)) -ne $expectedParent) { throw 'Unexpected publish output directory.' }
if (Test-Path -LiteralPath $output) {
    $existing = Get-Item -LiteralPath $output
    if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish output must not be a symbolic link or junction.' }
    Remove-Item -LiteralPath $existing.FullName -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained $contained -p:PublishSingleFile=$single -p:IncludeNativeLibrariesForSelfExtract=$single -p:EnableCompressionInSingleFile=$single -p:PublishTrimmed=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $output -Recurse -Force
$docsOutput = Join-Path $output 'docs'
New-Item -ItemType Directory -Force -Path $docsOutput | Out-Null
foreach ($document in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -Filter 'README.*.md') {
    Copy-Item -LiteralPath $document.FullName -Destination $docsOutput -Force
}
foreach ($name in @('LOCALIZATION.md', 'DEVELOPMENT.md', 'languages.json')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('docs/' + $name)) -Destination $docsOutput -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/images') -Destination $docsOutput -Recurse -Force
Write-Host "Published to $output"
