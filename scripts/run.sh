#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

dotnet run --project AutoYnabCsv.ConsoleApp "$@"
