param([switch]$Check)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$languages = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/languages.json') -Raw -Encoding utf8 | ConvertFrom-Json
$start = '<!-- languages:start -->'
$end = '<!-- languages:end -->'
$pattern = '(?s)' + [regex]::Escape($start) + '.*?' + [regex]::Escape($end)
$outdated = @()
foreach ($language in $languages) {
    $path = Join-Path $projectRoot $language.path
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    if (-not [regex]::IsMatch($text, $pattern)) { throw "Missing language navigation markers in $($language.path)" }
    $links = foreach ($entry in $languages) {
        if ($entry.code -eq $language.code) { '**' + $entry.label + '**' }
        else {
            $directoryUri = [uri]([IO.Path]::GetDirectoryName($path) + [IO.Path]::DirectorySeparatorChar)
            $relative = [uri]::UnescapeDataString($directoryUri.MakeRelativeUri([uri](Join-Path $projectRoot $entry.path)).ToString())
            '[' + $entry.label + '](' + $relative + ')'
        }
    }
    $block = $start + "`n" + ($links -join ' · ') + "`n" + $end
    $updated = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $block })
    if ($text -ne $updated) {
        $outdated += $language.path
        if (-not $Check) { [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false)) }
    }
}
if ($Check -and $outdated.Count) { throw ('Outdated README language navigation: ' + ($outdated -join ', ')) }
Write-Host "README language navigation verified for $($languages.Count) languages."
