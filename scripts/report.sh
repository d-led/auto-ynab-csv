#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Writes the searchable report and the YNAB CSVs for one folder of exports.
# The report lands in the export folder itself: {folder}/index.html
# Usage: ./scripts/report.sh <folder-or-file> [more mail export options]
#   ./scripts/report.sh ~/Downloads --search

invoked_from="$PWD"
cd "$(dirname "${BASH_SOURCE[0]}")/.."

if [ "$#" -eq 0 ]; then
  echo "usage: ./scripts/report.sh <folder-or-file> [mail export options...]" >&2
  echo "       ./scripts/report.sh --input <folder-or-file> [options...]" >&2
  exit 2
fi

# A relative path is read from where it was typed, not from the repository root.
resolve() { case "$1" in /*) printf '%s' "$1" ;; *) printf '%s' "$invoked_from/$1" ;; esac; }

if [[ "$1" == --input ]]; then
  input="$(resolve "${2:?--input needs a path}")"
  shift 2
  dotnet run --project AutoYnabCsv.ConsoleApp -- mail export --input "$input" "$@"
elif [[ "$1" == -* ]]; then
  dotnet run --project AutoYnabCsv.ConsoleApp -- mail export "$@"
else
  input="$(resolve "$1")"
  shift
  dotnet run --project AutoYnabCsv.ConsoleApp -- mail export --input "$input" "$@"
fi
