param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$sourceRoot = Join-Path $RootPath "src"
if (-not (Test-Path $sourceRoot)) {
    Write-Host "Skip encoding validation: src folder not found."
    exit 0
}

$patterns = @(
    [string]([char]0x00C3),                           # Ã
    [string]([char]0x00C4),                           # Ä
    ([string]([char]0x00E1) + [string]([char]0x00BB)), # á»
    ([string]([char]0x00E1) + [string]([char]0x00BA)), # áº
    [string]([char]0x00C2),                           # Â
    [string]([char]0xFFFD)                            # replacement char
)

$include = @("*.cs", "*.xaml", "*.resx", "*.json", "*.md", "*.ps1")
$files = Get-ChildItem -Path $sourceRoot -Recurse -File -Include $include |
    Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" }

$violations = @()

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    foreach ($pattern in $patterns) {
        if ($content.Contains($pattern)) {
            $violations += $file.FullName
            break
        }
    }
}

if ($violations.Count -gt 0) {
    $uniqueViolations = $violations | Sort-Object -Unique
    Write-Error ("Detected possible mojibake strings in source files:`n" + ($uniqueViolations -join "`n"))
    exit 1
}

Write-Host "Encoding validation passed."
exit 0
