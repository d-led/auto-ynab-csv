#!/usr/bin/env bash
# The check in one word: read the plan from YNAB, then hold it against the bank's exports.
#
#   ./scripts/check.sh ~/Downloads/exports
#
# Your account mapping is typed once and kept in
#   ~/.config/auto-ynab-csv/accounts.txt
# one "label=id" per line, blank lines and lines starting with # ignored. The labels are printed by
# the first run of this script: it names the exports it could not place. Three prefixed lines are
# understood as well: plan=<id> (so YNAB_PLAN_ID is not needed), own-iban=<label>=<number> and
# expect-balance=<label>=<amount> (a balance an export does not carry).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exports="${1:?usage: check.sh <exports-folder> [out-folder]}"
out="${2:-$here/artifacts/ynab/$(date +%Y%m%d-%H%M%S)}"
mapping="${XDG_CONFIG_HOME:-$HOME/.config}/auto-ynab-csv/accounts.txt"

app=(dotnet run --project "$here/AutoYnabCsv.ConsoleApp" --)

options=()
plan_id="${YNAB_PLAN_ID:-}"
if [[ -f "$mapping" ]]; then
    while IFS= read -r line; do
        [[ -z "${line// }" || "$line" == \#* ]] && continue
        case "$line" in
            plan=*) plan_id="${line#plan=}" ;;
            account=*) options+=(--account "${line#account=}") ;;
            own-iban=*) options+=(--own-iban "${line#own-iban=}") ;;
            expect-balance=*) options+=(--expect-balance "${line#expect-balance=}") ;;
            *) options+=(--account "$line") ;;
        esac
    done <"$mapping"
else
    echo "No account mapping yet at $mapping — this run will name the exports it cannot place." >&2
fi

plan_id="${plan_id:?set YNAB_PLAN_ID, or add a plan=<id> line to $mapping}"

# A snapshot already in the folder means the plan can be read from it again: re-planning after a change
# to the rules costs nothing. Only an empty folder spends the request.
if [[ -f "$out/snapshot.json" ]]; then
    echo "== reusing the snapshot in $out — no request spent"
else
    echo "== reading the plan (costs about one request per month of history)"
    "${app[@]}" ynab snapshot --plan "$plan_id" --out "$out" --since "${YNAB_SINCE:-2024-01-01}"
fi

echo
echo "== holding it against $exports"
"${app[@]}" ynab reconcile --snapshot "$out" --input "$exports" ${options[@]+"${options[@]}"}

echo
echo "read:   $out/reconcile.md   (the first table, the difference column)"
echo "plan:   $out/plan.json     (apply it with the command in docs/workflow.md)"
