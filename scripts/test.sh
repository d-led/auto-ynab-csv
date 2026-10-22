#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# Runs the test suite. Extra arguments go to `dotnet test`.
# Usage: ./scripts/test.sh [--filter Name]

cd "$(dirname "${BASH_SOURCE[0]}")/.."
dotnet test AutoYnabCsv.Tests/AutoYnabCsv.Tests.csproj --nologo "$@"
