#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Puts the two programs on the PATH, so the commands in the README are the commands you type.
#
#   ./scripts/install.sh                       # from the newest local package, or packs one
#   ./scripts/install.sh --from <tgz|zip>      # from a release archive you downloaded
#   ./scripts/install.sh --prefix ~/bin        # somewhere else than ~/.local/bin
#
# It installs `auto-ynab-csv` and `auto-ynab-csv-mcp`, never anything else, and says what it did and
# whether the place it chose is on your PATH.

cd "$(dirname "${BASH_SOURCE[0]}")/.."

prefix="${HOME}/.local/bin"
archive=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --prefix) prefix="${2:?--prefix needs a directory}"; shift 2 ;;
    --from)   archive="${2:?--from needs an archive}"; shift 2 ;;
    -h|--help)
      echo "usage: ./scripts/install.sh [--prefix DIR] [--from ARCHIVE]"
      exit 0
      ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

case "$(uname -s)" in
  Darwin) rid="osx-$( [ "$(uname -m)" = "arm64" ] && echo arm64 || echo x64 )" ;;
  Linux)  rid="linux-x64" ;;
  *)      rid="win-x64" ;;
esac

staged=""
if [ -n "$archive" ]; then
  # A downloaded release archive holds one folder with the programs inside it.
  unpack="$(mktemp -d)"
  trap 'rm -rf "$unpack"' EXIT
  case "$archive" in
    *.zip) unzip -q "$archive" -d "$unpack" ;;
    *.tgz|*.tar.gz) tar xzf "$archive" -C "$unpack" ;;
    *) echo "not an archive this knows how to open: $archive" >&2; exit 2 ;;
  esac
  staged="$(dirname "$(find "$unpack" -type f \( -name auto-ynab-csv -o -name auto-ynab-csv.exe \) | head -n1)")"
else
  # The newest local package, or one packed now. Nothing is downloaded.
  version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' AutoYnabCsv.ConsoleApp/AutoYnabCsv.ConsoleApp.csproj | head -n1)"
  version="${version:-0.0.0}"
  staged="publish/$version/auto-ynab-csv-$version-$rid"

  if [ ! -x "$staged/auto-ynab-csv" ] && [ ! -f "$staged/auto-ynab-csv.exe" ]; then
    echo "== packing $rid first (this builds, it does not download)"
    ./scripts/package.sh "$rid"
  fi
fi

if [ -z "$staged" ] || [ ! -d "$staged" ]; then
  echo "could not find the programs to install; pass --from <archive> or run ./scripts/package.sh" >&2
  exit 1
fi

mkdir -p "$prefix"
installed=()
for program in auto-ynab-csv auto-ynab-csv-mcp; do
  source="$staged/$program"
  [ -f "$source.exe" ] && source="$source.exe"
  if [ ! -f "$source" ]; then
    echo "missing from the package: $program" >&2
    exit 1
  fi
  cp -p -f "$source" "$prefix/"
  chmod +x "$prefix/$(basename "$source")"
  installed+=("$prefix/$(basename "$source")")
done

echo "installed:"
for path in "${installed[@]}"; do
  echo "  $path"
done

case ":${PATH}:" in
  *":$prefix:"*)
    echo
    echo "$prefix is on your PATH. Try: auto-ynab-csv --version"
    ;;
  *)
    echo
    echo "$prefix is NOT on your PATH. Add it once, in the shell you start your work in:"
    echo "  echo 'export PATH=\"$prefix:\$PATH\"' >> ~/.zshrc"
    echo "  source ~/.zshrc"
    echo
    echo "Until then, use the full path: $prefix/auto-ynab-csv --version"
    ;;
esac
