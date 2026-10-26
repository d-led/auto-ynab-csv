#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Converts bank exports: the YNAB CSV is written next to each original, and one report
# describes the batch. A folder is one batch; a file is a batch of one. Anything after the
# first path is passed to the command, e.g. --search.
#
# Two shapes, both correct:
#   ./scripts/convert.sh <file-or-folder> [options]
#   ./scripts/convert.sh --input <file-or-folder> [options]
#
# A relative path is read from the directory you typed it in, not from the repository the script
# moves into: `convert.sh ../tmp/exports` means what it says.

invoked_from="$PWD"
cd "$(dirname "${BASH_SOURCE[0]}")/.."

if [ "$#" -eq 0 ]; then
  echo "usage: ./scripts/convert.sh <file-or-folder> [options]" >&2
  echo "       ./scripts/convert.sh --input <file-or-folder> [options]" >&2
  exit 2
fi

# Where the user meant, whatever directory this script then works from.
resolve() { case "$1" in /*) printf '%s' "$1" ;; *) printf '%s' "$invoked_from/$1" ;; esac; }

if [[ "$1" == --input ]]; then
  input="$(resolve "${2:?--input needs a path}")"
  shift 2
  dotnet run --project AutoYnabCsv.ConsoleApp -- convert --input "$input" "$@"
elif [[ "$1" == -* ]]; then
  dotnet run --project AutoYnabCsv.ConsoleApp -- convert "$@"
else
  input="$(resolve "$1")"
  shift
  dotnet run --project AutoYnabCsv.ConsoleApp -- convert --input "$input" "$@"
fi
