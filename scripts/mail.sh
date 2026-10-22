#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Every mail command: sync, scan, correlate, export, transfers, patterns, where.
# Usage: ./scripts/mail.sh <command> [options]

cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet run --project AutoYnabCsv.ConsoleApp -- mail "$@"
