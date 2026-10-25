#!/usr/bin/env bash
# The check in one word: read the plan from YNAB, then hold it against the bank's exports.
#
#   ./scripts/check.sh ~/Downloads/exports
#
# Your account mapping is typed once and kept in
#   ~/.config/auto-ynab-csv/accounts.txt
# one "label=id" per line, blank lines and lines starting with # ignored. The labels are printed by
# the first run of this script: it names the exports it could not place.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exports="${1:?usage: check.sh <exports-folder> [out-folder]}"
out="${2:-$here/artifacts/ynab/$(date +%Y%m%d-%H%M%S)}"
mapping="${XDG_CONFIG_HOME:-$HOME/.config}/auto-ynab-csv/accounts.txt"

app=(dotnet run --project "$here/AutoYnabCsv.ConsoleApp" --)
plan_id="${YNAB_PLAN_ID:?set YNAB_PLAN_ID to the plan id, or run: dotnet run --project AutoYnabCsv.ConsoleApp -- ynab plans}"

mapped=()
if [[ -f "$mapping" ]]; then
    while IFS= read -r line; do
        [[ -z "${line// }" || "$line" == \#* ]] && continue
        mapped+=(--account "$line")
    done <"$mapping"
else
    echo "No account mapping yet at $mapping — this run will name the exports it cannot place." >&2
fi

echo "== reading the plan (costs about one request per month of history)"
"${app[@]}" ynab snapshot --plan "$plan_id" --out "$out" --since "${YNAB_SINCE:-2024-01-01}"

echo
echo "== holding it against $exports"
"${app[@]}" ynab reconcile --snapshot "$out" --input "$exports" ${mapped[@]+"${mapped[@]}"}

echo
echo "read:   $out/reconcile.md   (the first table, the difference column)"
echo "plan:   $out/plan.json     (apply it with the command in docs/workflow.md)"
