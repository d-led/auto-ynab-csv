#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Every mail command: sync, scan, correlate, export, transfers, patterns, where.
# Usage: ./scripts/mail.ps1 <command> [options]

Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project AutoYnabCsv.ConsoleApp -- mail @args
