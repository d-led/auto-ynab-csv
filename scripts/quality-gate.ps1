#!/usr/bin/env pwsh
$ErrorActionPreference = 'Continue'

# The quality gate: formatting, compiler warnings, tests with coverage, CRAP per method, and
# duplicate code. Every step runs even when an earlier one fails, then the exit code says
# whether anything has to be fixed. Optional tools are skipped with a note, never guessed.
#
# Usage: ./scripts/quality-gate.ps1 [-CrapThreshold 30] [-Duplication 3] [-SkipFormat] [-StrictFormat] [-SkipDuplicates]

param(
    [double]$CrapThreshold = 30,
    [double]$Duplication = 3,
    [switch]$SkipFormat,
    [switch]$StrictFormat,
    [switch]$SkipDuplicates
)

Set-Location (Join-Path $PSScriptRoot '..')

$artifacts = 'artifacts/quality-gate'
$coverage = "$artifacts/coverage"
if (Test-Path $coverage) { Remove-Item $coverage -Recurse -Force }
New-Item -ItemType Directory -Path $coverage -Force | Out-Null

$failures = 0
$skipped = 0

function Pass($name) { Write-Host "PASS  $name" }
function Fail($name) { Write-Host "FAIL  $name"; $script:failures++ }
function Skip($name, $why) { Write-Host "SKIP  $name ($why)"; $script:skipped++ }
function Step($name) { Write-Host ""; Write-Host "== $name" }

Step 'format'
if ($SkipFormat) { Skip 'format' 'asked to' }
elseif (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Skip 'format' 'no dotnet' }
elseif (dotnet format --verify-no-changes) { Pass 'format' }
elseif ($StrictFormat) { Fail 'format' }
else { Write-Host "ADVICE  formatting differs; run 'dotnet format' or pass -StrictFormat" }

Step 'compiler warnings'
if (dotnet build auto-ynab-csv.sln --nologo --no-incremental -warnaserror) { Pass 'build without warnings' }
else { Fail 'build without warnings' }

Step 'tests and coverage'
if (dotnet test AutoYnabCsv.Tests/AutoYnabCsv.Tests.csproj --nologo `
        --collect:'XPlat Code Coverage' `
        --results-directory $coverage) {
    Pass 'tests'
}
else {
    Fail 'tests'
}

Step 'CRAP per method'
$report = Get-ChildItem -Path $coverage -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $report) {
    Skip 'CRAP' 'no coverage report'
}
else {
    $xml = [xml](Get-Content $report.FullName)
    $scored = foreach ($class in $xml.coverage.packages.package.classes.class) {
        foreach ($method in $class.methods.method) {
            $lines = @($method.lines.line)
            $covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
            $rate = if ($lines.Count -eq 0) { 1.0 } else { $covered / $lines.Count }
            $complexity = if ($method.complexity) { [int]$method.complexity } else { 1 }
            [pscustomobject]@{
                Crap       = [math]::Round($complexity * $complexity * [math]::Pow(1 - $rate, 3) + $complexity, 1)
                Complexity = $complexity
                Coverage   = $rate * 100
                Method     = "$($class.name).$($method.name)"
                File       = $class.filename
            }
        }
    }

    $sorted = $scored | Sort-Object Crap -Descending
    $sorted | Select-Object -First 15 |
        Format-Table -AutoSize Crap, Complexity, @{ n = 'Coverage'; e = { '{0,5:N1}%' -f $_.Coverage } }, Method

    $over = @($sorted | Where-Object { $_.Crap -gt $CrapThreshold })
    Write-Host "$($sorted.Count) methods, $($over.Count) above CRAP $CrapThreshold"
    if ($over.Count -eq 0) { Pass 'CRAP' } else { Fail 'CRAP' }
}

Step 'duplicate code'
if ($SkipDuplicates) { Skip 'duplicates' 'asked to' }
elseif (-not (Get-Command npx -ErrorAction SilentlyContinue)) { Skip 'duplicates' 'no npx (install Node.js)' }
elseif (npx --yes jscpd --min-tokens 60 --threshold $Duplication --reporters console,html `
        --output "$artifacts/jscpd" `
        --ignore '**/obj/**,**/bin/**,**/publish/**,**/TestResults/**,**/artifacts/**' .) {
    Pass 'jscpd'
}
else {
    Fail 'jscpd'
}

Write-Host ""
Write-Host "quality gate: $failures failing, $skipped skipped. Reports in $artifacts/"
exit $failures
