#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Publishes the CLI and the desktop app.
# Usage: ./scripts/build.ps1

Set-Location (Join-Path $PSScriptRoot '..')
dotnet publish AutoYnabCsv.ConsoleApp -c Release
dotnet publish AutoYnabCsv.DesktopApp -c Release
