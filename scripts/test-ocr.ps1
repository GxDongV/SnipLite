param([switch]$SkipDownload)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$imageDirectory = Join-Path $projectRoot '.local\ocr-smoke\images'
$null = New-Item -ItemType Directory -Path $imageDirectory -Force
# Pin upstream inputs; do not put downloaded third-party pictures in release packages.
$source = 'https://raw.githubusercontent.com/RapidAI/RapidOCR/a538d3a75e26ca7ac521da8602842b9d6e5a872d/python/tests/test_files'
$samples = @(
    @{ Name = 'text_rec.jpg'; Hash = '05921C624274EEDBFC1208D12EBA5B06ACBDD1291D1B1A2129770199F430D6AE' },
    @{ Name = 'en_rec.jpg'; Hash = '4E92CE907F803BF1BC2B1D026DBB22A6C81CD97A932484AD1070C9FDE8F013BC' },
    @{ Name = 'short.png'; Hash = 'F893CC42BF607ACA25C411096E5A7BFBA6BD647DCCAD20287A32A8ED7084C916' },
    @{ Name = 'text_cls.jpg'; Hash = '872200F57A1408E7AAB2856D5F2C687B3A937805E0C4FF74BD7DE21DF1F742B9' }
)
foreach ($sample in $samples) {
    $path = Join-Path $imageDirectory $sample.Name
    if (!(Test-Path -LiteralPath $path)) {
        if ($SkipDownload) { throw "Missing cached sample: $path" }
        Invoke-WebRequest -Uri "$source/$($sample.Name)" -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sample.Hash) {
        throw "Sample hash mismatch: $path"
    }
}
$env:SNIPLITE_TEST_OUTPUT = Join-Path $projectRoot 'artifacts\ocr-smoke'
dotnet run --project (Join-Path $projectRoot 'tests\SnipLite.SelfTest') -c Release -- --ocr-smoke $imageDirectory
if ($LASTEXITCODE -ne 0) { throw "OCR smoke check failed. See $env:SNIPLITE_TEST_OUTPUT" }
