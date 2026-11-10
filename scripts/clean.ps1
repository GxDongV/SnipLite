$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$targets = @(
    'artifacts', 'bin', 'obj',
    'src\SnipLite\bin', 'src\SnipLite\obj',
    'tests\SnipLite.SelfTest\bin', 'tests\SnipLite.SelfTest\obj'
)
foreach ($relativePath in $targets) {
    $targetPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
    if (-not $targetPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Clean target escapes the project: $targetPath"
    }
    if (Test-Path -LiteralPath $targetPath) {
        if ((Get-Item -LiteralPath $targetPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to recursively remove a reparse point: $targetPath"
        }
        Remove-Item -LiteralPath $targetPath -Recurse -Force
        Write-Host "Removed $relativePath"
    }
}
