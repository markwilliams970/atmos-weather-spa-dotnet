#!/usr/bin/env bash
#
# Runs scripts/lab/vm-teardown.ps1 against the win2025app lab VM over WinRM,
# from this Linux dev machine, via the shared connection helpers in
# _winrm-common.sh.
#
# Defaults to a DRY RUN (prints what's on the VM and what would be removed).
# Pass --force to actually tear it down.
#
# Usage:
#   scripts/lab/run-vm-teardown.sh              # dry run (default)
#   scripts/lab/run-vm-teardown.sh --force       # actually tear down
#
# Connection details default to this lab's known, intentionally non-secret
# values (see CLAUDE.md/project notes) but can be overridden:
#   ATMOS_VM_HOST=192.168.122.54
#   ATMOS_VM_USER=Administrator
#   ATMOS_VM_PASSWORD=TestP@ssw0rd123

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEARDOWN_PS1="$REPO_ROOT/scripts/lab/vm-teardown.ps1"

source "$REPO_ROOT/scripts/lab/_winrm-common.sh"

FORCE_FLAG=""
for arg in "$@"; do
  case "$arg" in
    --force) FORCE_FLAG="-Force" ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--force]" >&2
      exit 2
      ;;
  esac
done

if [[ ! -f "$TEARDOWN_PS1" ]]; then
  echo "Cannot find $TEARDOWN_PS1" >&2
  exit 1
fi

winrm_init
trap winrm_cleanup EXIT

echo "Running vm-teardown.ps1 against $ATMOS_VM_HOST as $ATMOS_VM_USER ($( [[ -n "$FORCE_FLAG" ]] && echo "FORCE" || echo "dry run" ))..."
echo

winrm_run_ps1 "$TEARDOWN_PS1" $FORCE_FLAG
