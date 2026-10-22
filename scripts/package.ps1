#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

# Packs one self-contained single-file artifact per target.
# Usage: ./scripts/package.ps1 [rid ...]
#   win-x64 win-arm64 osx-arm64 osx-x64 linux-x64
# With no argument it packs for the machine it runs on.

Set-Location (Join-Path $PSScriptRoot '..')

$project = 'AutoYnabCsv.ConsoleApp/AutoYnabCsv.ConsoleApp.csproj'
$version = (Select-String -Path $project -Pattern '<Version>(.*)</Version>').Matches.Groups[1].Value
if (-not $version) { $version = '0.0.0' }
$out = "publish/$version"

$rids = @($args)
if ($rids.Count -eq 0) {
    $rids = if ($IsWindows) { @('win-x64') }
    elseif ($IsMacOS) { @('osx-arm64') }
    else { @('linux-x64') }
}

function Publish-Project($name, $rid) {
    dotnet publish $name -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -o "$out/$rid"
}

foreach ($rid in $rids) {
    Write-Host "== $rid"

    Publish-Project AutoYnabCsv.ConsoleApp $rid
    Publish-Project AutoYnabCsv.DesktopApp $rid

    Compress-Archive -Path "$out/$rid/*" -DestinationPath "$out/auto-ynab-csv-$version-$rid.zip" -Force
}

Write-Host "Artifacts in $out"
