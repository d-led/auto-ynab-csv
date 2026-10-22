#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Writes the searchable report and the YNAB CSVs for one folder of exports.
# The report lands in the export folder itself: {folder}/index.html
# Usage: ./scripts/report.sh <folder-or-file> [more mail export options]
#   ./scripts/report.sh ~/Downloads --search

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if [ "$#" -eq 0 ]; then
  echo "usage: ./scripts/report.sh <folder-or-file> [mail export options...]" >&2
  exit 2
fi

input="$1"
shift

dotnet run --project AutoYnabCsv.ConsoleApp -- mail export --input "$input" "$@"
