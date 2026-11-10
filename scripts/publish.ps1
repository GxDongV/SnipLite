param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectRoot 'src\SnipLite\SnipLite.csproj'
$flavor = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$output = Join-Path $projectRoot "artifacts\publish\win-x64\$flavor"
$contained = if ($SelfContained) { 'true' } else { 'false' }

dotnet publish $project -c Release -r win-x64 --self-contained $contained -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $output -Recurse -Force
Write-Host "Published to $output"
