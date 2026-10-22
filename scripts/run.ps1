#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Runs the CLI. Extra arguments are passed through.
# Usage: ./scripts/run.ps1 [cli arguments]

Set-Location (Join-Path $PSScriptRoot '..')
dotnet run --project AutoYnabCsv.ConsoleApp -- @args
