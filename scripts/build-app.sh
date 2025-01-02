#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp -property:Configuration=Release -p:RuntimeIdentifier=osx-arm64 -p:UseAppHost=true
dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp -property:Configuration=Release -p:RuntimeIdentifier=osx-x64 -p:UseAppHost=true
