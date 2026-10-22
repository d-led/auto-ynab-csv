#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Runs the test suite. Extra arguments go to `dotnet test`.
# Usage: ./scripts/test.ps1 [-Filter Name]

Set-Location (Join-Path $PSScriptRoot '..')
dotnet test AutoYnabCsv.Tests/AutoYnabCsv.Tests.csproj --nologo @args
