#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# requires https://github.com/idesis-gmbh/png2icons to be available as png2icons-cli

SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

cd "${SCRIPT_DIR}/../AutoYnabCsv.DesktopApp/Resources/Icons"

png2icons-cli icon-source.png app-icon -allwe
