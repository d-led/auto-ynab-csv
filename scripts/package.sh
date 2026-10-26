#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Packs self-contained single-file artifacts per target, in two archives:
#
#   auto-ynab-csv-<version>-<rid>          the two programs a person needs
#     auto-ynab-csv                        the command line: convert, check, repair
#     auto-ynab-csv-mcp                    the server an editor starts
#   auto-ynab-csv-desktop-<version>-<rid>  the window, for those who want it
#
# Neither needs .NET installed. They are separate because the app is over half the weight and nearly
# nobody downloading a command line wants a GUI with it.
#
# Usage: ./scripts/package.sh [rid ...]
#   osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-musl-arm64
# With no argument it packs for the machine it runs on. Windows targets get a .zip, the rest a .tgz.

cd "$(dirname "${BASH_SOURCE[0]}")/.."

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' AutoYnabCsv.ConsoleApp/AutoYnabCsv.ConsoleApp.csproj | head -n1)"
version="${version:-0.0.0}"
out="publish/$version"

rids=("$@")
if [ ${#rids[@]} -eq 0 ]; then
  case "$(uname -s)" in
    Darwin) rids=(osx-arm64) ;;
    Linux)  rids=(linux-x64) ;;
    *)      rids=(win-x64) ;;
  esac
fi

publish() { # project, rid, into
  dotnet publish "$1" -c Release -r "$2" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    -o "$3"
}

pack() { # source folder, archive name without extension, rid
  local source="$1" name="$2" rid="$3"
  if [[ "$rid" == win-* ]]; then
    if command -v 7z >/dev/null 2>&1; then
      7z a -tzip "$out/$name.zip" "$source" >/dev/null
    else
      (cd "$out" && zip -qr "$name.zip" "$(basename "$source")")
    fi
  else
    (cd "$out" && tar czf "$name.tgz" "$(basename "$source")")
  fi
}

for rid in "${rids[@]}"; do
  echo "== $rid"

  cli="$out/auto-ynab-csv-$version-$rid"
  desktop="$out/auto-ynab-csv-desktop-$version-$rid"
  rm -rf "$cli" "$desktop"

  publish AutoYnabCsv.ConsoleApp "$rid" "$cli"
  publish AutoYnabCsv.Mcp "$rid" "$cli"
  publish AutoYnabCsv.DesktopApp "$rid" "$desktop"

  # Nobody downloading a program wants its debug symbols in the same folder.
  rm -f "$cli"/*.pdb "$desktop"/*.pdb

  if [[ "$rid" == osx-* ]]; then
    # macOS needs a bundle: window identity, icons, and stable privacy grants.
    dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp \
      -property:Configuration=Release \
      -property:RuntimeIdentifier="$rid" \
      -property:SelfContained=true \
      -property:UseAppHost=true

    bundle="AutoYnabCsv.DesktopApp/bin/Release/net10.0/$rid/publish/AutoYnabCsv.DesktopApp.app"
    if [ -d "$bundle" ]; then
      cp -R "$bundle" "$desktop/"
      # The bundle is how the window is launched; the loose binary beside it is a second, confusing way.
      rm -f "$desktop/AutoYnabCsv.DesktopApp"
    fi
  fi

  pack "$cli" "auto-ynab-csv-$version-$rid" "$rid"
  pack "$desktop" "auto-ynab-csv-desktop-$version-$rid" "$rid"
done

echo "Artifacts in $out"
echo
echo "Note: packing a runtime writes that runtime into each packages.lock.json it touched."
echo "Put them back before committing: git checkout -- '*/packages.lock.json'"
