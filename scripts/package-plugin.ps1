param(
    [string]$Project = 'src/SnipLite.ScrollCapture/SnipLite.ScrollCapture.csproj',
    [string]$OutputDirectory = '.local/plugin-packages'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Project))
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
dotnet build $projectPath -c Release --nologo -m:1 -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
$projectFolder = Split-Path -Parent $projectPath
$manifest = Get-Content -LiteralPath (Join-Path $projectFolder 'plugin.json') -Raw | ConvertFrom-Json
$build = Join-Path $projectFolder 'bin/Release/net10.0-windows'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$package = Join-Path $output "$($manifest.id)-$($manifest.version).snipplugin"
if (Test-Path -LiteralPath $package) { throw "Package already exists: $package. Choose another output directory." }
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $build -File -Recurse) {
        if ($file.Extension -eq '.pdb' -or $file.Name -eq 'SnipLite.Plugin.Abstractions.dll') { continue }
        $relative = [IO.Path]::GetRelativePath($build, $file.FullName).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Host "Plugin package: $package"
