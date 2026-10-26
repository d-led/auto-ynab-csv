#!/usr/bin/env bash
# Runs the MCP server from this checkout, built as it is right now: an editor that starts it gets the
# latest code, not whatever a release binary was when it was downloaded.
#
# The build is chatty on stdout, and stdout is the protocol channel — one MSBuild line and the
# client's first message is garbage. So the build's noise goes to stderr, where a log line belongs,
# and only the server writes to stdout. A second start rebuilds nothing: the build is incremental.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo/AutoYnabCsv.Mcp/AutoYnabCsv.Mcp.csproj"

dotnet build "$project" --nologo -v q 1>&2
exec dotnet "$repo/AutoYnabCsv.Mcp/bin/Debug/net10.0/auto-ynab-csv-mcp.dll" "$@"
