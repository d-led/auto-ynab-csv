#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Converts bank exports: the YNAB CSV is written next to each original, and one report
# describes the batch. A folder is one batch; a file is a batch of one. Anything after the
# first path is passed to the command, e.g. -Search.
# Usage: ./scripts/convert.ps1 <file-or-folder> [options]

Set-Location (Join-Path $PSScriptRoot '..')

if ($args.Count -eq 0) {
    Write-Error 'usage: ./scripts/convert.ps1 <file-or-folder> [options]'
    exit 2
}

$input = $args[0]
$rest = $args | Select-Object -Skip 1

dotnet run --project AutoYnabCsv.ConsoleApp -- convert --input $input @rest
