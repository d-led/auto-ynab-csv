#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Writes the searchable report and the YNAB CSVs for one folder of exports.
# The report lands in the export folder itself: {folder}/index.html
# Usage: ./scripts/report.ps1 <folder-or-file> [more mail export options]
#   ./scripts/report.ps1 $HOME/Downloads -Search

Set-Location (Join-Path $PSScriptRoot '..')

if ($args.Count -eq 0) {
    Write-Error 'usage: ./scripts/report.ps1 <folder-or-file> [mail export options...]'
    exit 2
}

$source = $args[0]
$rest = $args | Select-Object -Skip 1

dotnet run --project AutoYnabCsv.ConsoleApp -- mail export --input $source @rest
