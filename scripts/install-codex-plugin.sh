#!/bin/bash
set -euo pipefail

if ! command -v python3 >/dev/null 2>&1; then
  echo 'Python 3 was not found in PATH. Install it before running this script.' >&2
  exit 1
fi
script_directory="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$script_directory/install-plugin.py" codex "$@"
