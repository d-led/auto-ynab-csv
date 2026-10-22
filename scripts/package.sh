#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Packs one self-contained single-file artifact per target.
# Usage: ./scripts/package.sh [rid ...]
#   osx-arm64 osx-x64 win-x64 win-arm64 linux-x64 linux-musl-arm64
# With no argument it packs for the machine it runs on.

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

publish() { # project, rid
  dotnet publish "$1" -c Release -r "$2" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    -o "$out/$2"
}

for rid in "${rids[@]}"; do
  echo "== $rid"

  publish AutoYnabCsv.ConsoleApp "$rid"
  publish AutoYnabCsv.DesktopApp "$rid"

  if [[ "$rid" == osx-* ]]; then
    # macOS needs a bundle: window identity, icons, and stable privacy grants.
    dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp \
      -property:Configuration=Release \
      -property:RuntimeIdentifier="$rid" \
      -property:SelfContained=true \
      -property:UseAppHost=true

    bundle="AutoYnabCsv.DesktopApp/bin/Release/net10.0/$rid/publish/AutoYnabCsv.DesktopApp.app"
    if [ -d "$bundle" ]; then
      cp -R "$bundle" "$out/$rid/"
    fi
  fi

  (cd "$out" && tar czf "auto-ynab-csv-$version-$rid.tgz" "$rid")
done

echo "Artifacts in $out"
