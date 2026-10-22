#!/bin/bash
set -uo pipefail
IFS=$'\n\t'

# The quality gate: formatting, compiler warnings, tests with coverage, CRAP per method, and
# duplicate code. Every step runs even when an earlier one fails, then the exit code says
# whether anything has to be fixed. Optional tools are skipped with a note, never guessed.
#
# Usage: ./scripts/quality-gate.sh [options]
#   --crap-threshold N   fail above this CRAP value (default 30)
#   --duplication N      fail above this duplicate-code percentage (default 3)
#   --skip-format        leave formatting out
#   --strict-format      also fail on formatting (advisory otherwise)
#   --skip-duplicates    leave jscpd out

cd "$(dirname "${BASH_SOURCE[0]}")/.."

crap_threshold=30
duplication=3
run_format=1
strict_format=0
run_duplicates=1

while [ "$#" -gt 0 ]; do
  case "$1" in
    --crap-threshold) crap_threshold="$2"; shift 2 ;;
    --duplication) duplication="$2"; shift 2 ;;
    --skip-format) run_format=0; shift ;;
    --strict-format) strict_format=1; shift ;;
    --skip-duplicates) run_duplicates=0; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

artifacts="artifacts/quality-gate"
coverage="$artifacts/coverage"
rm -rf "$coverage"
mkdir -p "$coverage"

failures=0
skipped=0

pass() { echo "PASS  $1"; }
fail() { echo "FAIL  $1"; failures=$((failures + 1)); }
skip() { echo "SKIP  $1 ($2)"; skipped=$((skipped + 1)); }

step() { echo; echo "== $1"; }

step "format"
if [ "$run_format" -eq 0 ]; then
  skip "format" "asked to"
elif ! command -v dotnet >/dev/null; then
  skip "format" "no dotnet"
elif dotnet format --verify-no-changes; then
  pass "format"
elif [ "$strict_format" -eq 1 ]; then
  fail "format"
else
  echo "ADVICE  formatting differs; run 'dotnet format' or pass --strict-format"
fi

step "compiler warnings"
if dotnet build auto-ynab-csv.sln --nologo --no-incremental -warnaserror; then
  pass "build without warnings"
else
  fail "build without warnings"
fi

step "tests and coverage"
if dotnet test AutoYnabCsv.Tests/AutoYnabCsv.Tests.csproj --nologo \
  --collect:"XPlat Code Coverage" \
  --results-directory "$coverage"; then
  pass "tests"
else
  fail "tests"
fi

step "CRAP per method"
report="$(find "$coverage" -name 'coverage.cobertura.xml' -print -quit 2>/dev/null || true)"
if [ -z "$report" ]; then
  skip "CRAP" "no coverage report"
elif ! command -v python3 >/dev/null; then
  skip "CRAP" "no python3"
elif python3 scripts/crap.py "$report" --threshold "$crap_threshold"; then
  pass "CRAP"
else
  fail "CRAP"
fi

step "duplicate code"
if [ "$run_duplicates" -eq 0 ]; then
  skip "duplicates" "asked to"
elif ! command -v npx >/dev/null; then
  skip "duplicates" "no npx (install Node.js)"
else
  if npx --yes jscpd \
    --min-tokens 60 \
    --threshold "$duplication" \
    --reporters console,html \
    --output "$artifacts/jscpd" \
    --ignore "**/obj/**,**/bin/**,**/publish/**,**/TestResults/**,**/artifacts/**" \
    .; then
    pass "jscpd"
  else
    fail "jscpd"
  fi
fi

echo
echo "quality gate: $failures failing, $skipped skipped. Reports in $artifacts/"
[ "$failures" -eq 0 ]
