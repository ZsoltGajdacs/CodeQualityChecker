#!/bin/bash
set -euo pipefail

script_directory="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$script_directory/install-plugin.sh" codex "$@"
