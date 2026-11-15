$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resourceRoot = Join-Path $projectRoot 'src/SnipLite/Localization/Resources'
$languages = Get-Content (Join-Path $projectRoot 'docs/languages.json') -Raw -Encoding utf8 | ConvertFrom-Json
function Read-ResourceMap([string]$Path) {
    $map = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $data = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($property in $data.PSObject.Properties) { $map.Add($property.Name, [string]$property.Value) }
    return ,$map
}
$sources = Read-ResourceMap (Join-Path $resourceRoot 'sources.json')
$reference = Read-ResourceMap (Join-Path $resourceRoot 'en-US.json')
$placeholder = '\{\d+(?:,[^}:]+)?(?::[^}]+)?\}'
foreach ($language in $languages) {
    $catalog = Read-ResourceMap (Join-Path $resourceRoot ($language.code + '.json'))
    if ($catalog.Count -ne $reference.Count) { throw "Resource count mismatch: $($language.code)" }
    foreach ($key in $reference.Keys) {
        if (-not $catalog.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($catalog[$key])) { throw "Missing $key in $($language.code)" }
        $expected = @([regex]::Matches($reference[$key], $placeholder).Value | Sort-Object)
        $actual = @([regex]::Matches($catalog[$key], $placeholder).Value | Sort-Object)
        if (($expected -join '|') -cne ($actual -join '|')) { throw "Placeholder mismatch: $($language.code) / $key" }
    }
    foreach ($source in $sources.Keys) {
        $key = $sources[$source]
        if (-not $catalog.ContainsKey($key)) { throw "Unknown resource: $key" }
        if ($source.Contains('|*.')) {
            $patterns = $source.Split('|') | Where-Object { $_.StartsWith('*.') }
            $translated = $catalog[$key].Split('|') | Where-Object { $_.StartsWith('*.') }
            if (($patterns -join '|') -cne ($translated -join '|')) { throw "Invalid file filter: $($language.code) / $key" }
        }
    }
    Write-Host "$($language.code): $($catalog.Count) resources verified."
}
& (Join-Path $PSScriptRoot 'update-readme-languages.ps1') -Check
