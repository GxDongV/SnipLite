param([switch]$Publish)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SnipLite\SnipLite.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$repository = 'GxDongV/SnipLite'
$tag = 'v' + $version
$assetDir = Join-Path $projectRoot "artifacts\release\$version"
$assetNames = @("SnipLite-$version-win-x64-single.exe", "SnipLite-$version-win-x64-setup.exe", "SnipLite-$version-win-x64-portable.zip", 'SHA256SUMS.txt')
foreach ($name in $assetNames) { if (-not (Test-Path -LiteralPath (Join-Path $assetDir $name))) { throw "Missing release asset: $name" } }
$notes = [IO.File]::ReadAllText((Join-Path $projectRoot "docs\releases\$version.md"))
$head = (git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine release commit.' }
if (git -C $projectRoot status --porcelain) { throw 'Commit source changes before creating a release.' }

# Keep credentials only in this process; do not print them or write them to disk.
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'
$credentialLines = @("protocol=https`nhost=github.com`n`n" | git credential fill)
if ($LASTEXITCODE -ne 0) { throw 'GitHub credentials are unavailable.' }
$credential = @{}
foreach ($line in $credentialLines) { $parts = $line -split '=', 2; if ($parts.Count -eq 2) { $credential[$parts[0]] = $parts[1] } }
if (-not $credential['password']) { throw 'GitHub credentials are unavailable.' }
$headers = @{ Authorization = 'Bearer ' + $credential['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'SnipLite-Release' }
$api = "https://api.github.com/repos/$repository"
$releases = Invoke-RestMethod -Uri "$api/releases" -Headers $headers
$matchingReleases = @($releases | Where-Object { $_.tag_name -eq $tag })
if ($matchingReleases.Count -gt 1) { throw 'Multiple matching releases found.' }
if ($matchingReleases.Count -eq 1) {
    $release = $matchingReleases[0]
    if (-not $release.draft) { throw 'This release is already public; refusing to overwrite it.' }
} else {
    $body = @{ tag_name = $tag; target_commitish = $head; name = "SnipLite $tag"; body = $notes; draft = $true; prerelease = $false } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "$api/releases" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body
}

foreach ($name in $assetNames) {
    $path = Join-Path $assetDir $name
    $existing = @($release.assets | Where-Object name -eq $name)
    if ($existing.Count -gt 0) {
        if ($existing.Count -ne 1 -or $existing[0].size -ne (Get-Item -LiteralPath $path).Length) { throw "Conflicting draft asset: $name" }
        $localDigest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing[0].digest -ne $localDigest) { throw "Draft asset digest mismatch: $name" }
        Write-Host "Already uploaded: $name"
        continue
    }
    Write-Host "Uploading: $name"
    $type = if ($name.EndsWith('.zip')) { 'application/zip' } elseif ($name.EndsWith('.txt')) { 'text/plain' } else { 'application/octet-stream' }
    $upload = $release.upload_url -replace '\{.*\}$', ''
    $url = $upload + '?name=' + [Uri]::EscapeDataString($name)
    $asset = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -ContentType $type -InFile $path -TimeoutSec 600
    if ($asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item -LiteralPath $path).Length) { throw "Upload verification failed: $name" }
    Write-Host "Uploaded: $name ($($asset.size) bytes)"
}

$release = Invoke-RestMethod -Uri "$api/releases/$($release.id)" -Headers $headers
foreach ($name in $assetNames) {
    $asset = @($release.assets | Where-Object name -eq $name)
    $digest = 'sha256:' + (Get-FileHash -LiteralPath (Join-Path $assetDir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].state -ne 'uploaded' -or $asset[0].digest -ne $digest) { throw "Remote asset verification failed: $name" }
}
if ($Publish) {
    $body = @{ draft = $false; make_latest = 'true'; body = $notes } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "$api/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body
}
Write-Host "Release URL: $($release.html_url)"
Write-Host "Draft: $($release.draft)"
