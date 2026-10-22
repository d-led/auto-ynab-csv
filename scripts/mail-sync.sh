#!/bin/bash
set -uo pipefail
IFS=$'\n\t'

# Indexes Apple Mail from source, for development.
# Reading ~/Library/Mail needs Full Disk Access for the program that runs this: a terminal usually
# has it, an editor's integrated terminal usually does not.
#
# Usage: ./scripts/mail-sync.sh [mail sync options]
#   ./scripts/mail-sync.sh --full            re-read every message
#   ./scripts/mail-sync.sh --via-terminal    retry inside Terminal.app, which may hold the grant

cd "$(dirname "${BASH_SOURCE[0]}")/.."

root="${HOME}/Library/Mail"
via_terminal=0
args=()

for argument in "$@"; do
  if [ "$argument" = "--via-terminal" ]; then
    via_terminal=1
  else
    args+=("$argument")
  fi
done

readable() { [ -d "$root" ] && ls "$root" >/dev/null 2>&1; }

if ! readable && [ "$via_terminal" -eq 1 ] && [ "$(uname -s)" = Darwin ]; then
  script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
  echo "Cannot read $root here. Continuing in Terminal.app, which may have Full Disk Access."
  open -a Terminal "$script"
  exit 0
fi

if ! readable; then
  cat <<TEXT
Cannot read $root from this process.

Grant Full Disk Access to the app that runs the command:
  System Settings -> Privacy & Security -> Full Disk Access -> add Terminal (or iTerm)
Then quit and reopen that app, because macOS reads the grant at launch.

Retry from Terminal.app:  ./scripts/mail-sync.sh --via-terminal
TEXT
  exit 1
fi

exec dotnet run --project AutoYnabCsv.ConsoleApp -- mail sync "${args[@]+"${args[@]}"}"
