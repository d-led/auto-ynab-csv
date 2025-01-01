#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

dotnet publish AutoYnabCsv.ConsoleApp -c Release
dotnet publish AutoYnabCsv.DesktopApp -c Release
