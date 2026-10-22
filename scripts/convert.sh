#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Converts bank exports: the YNAB CSV is written next to each original, and one report
# describes the batch. A folder is one batch; a file is a batch of one. Anything after the
# first path is passed to the command, e.g. --search.
# Usage: ./scripts/convert.sh <file-or-folder> [options]

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if [ "$#" -eq 0 ]; then
  echo "usage: ./scripts/convert.sh <file-or-folder> [options]" >&2
  exit 2
fi

input="$1"
shift

dotnet run --project AutoYnabCsv.ConsoleApp -- convert --input "$input" "$@"
