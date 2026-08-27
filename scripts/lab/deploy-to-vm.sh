#!/usr/bin/env bash
#
# CREATE counterpart to run-vm-teardown.sh — deploys Atmos Weather to the
# win2025app lab VM: publishes the app, generates the current EF Core
# migration script, bundles both, serves them to the VM over a temporary
# local HTTP bridge (the file-transfer approach documented in
# docs/phase-c-build-environment.md's "File transfer note"), and runs
# scripts/lab/vm-create.ps1 on the VM over WinRM to actually apply
# everything: IIS prerequisites, the SQL Server persistence layer, the
# files themselves, web.config, the log directory, and the firewall rule.
#
# Defaults to a DRY RUN: builds and publishes the app and generates the
# migration script (so real compile/migration errors surface here, not
# mid-deploy), then asks vm-create.ps1 to report current VM state — no
# bundle is transferred and nothing on the VM changes. Pass --force to
# actually deploy.
#
# Usage:
#   scripts/lab/deploy-to-vm.sh              # dry run (default)
#   scripts/lab/deploy-to-vm.sh --force       # actually deploy
#
# Connection details default to this lab's known, intentionally non-secret
# values (see CLAUDE.md/project notes) but can be overridden:
#   ATMOS_VM_HOST=192.168.122.54
#   ATMOS_VM_USER=Administrator
#   ATMOS_VM_PASSWORD=TestP@ssw0rd123
#
# The atmos_app SQL login's password is randomly generated per run unless
# overridden with ATMOS_SQL_ATMOS_APP_PASSWORD — vm-create.ps1 resets it to
# whatever this run uses and writes the matching connection string into
# web.config in the same run, so there's never a need to know or recover an
# old value (same reasoning as dev-harness-create.sh's sa password).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CREATE_PS1="$REPO_ROOT/scripts/lab/vm-create.ps1"
WEB_PROJECT="$REPO_ROOT/src/Atmos.Web"

source "$REPO_ROOT/scripts/lab/_winrm-common.sh"

BRIDGE_IP="192.168.122.1"
BRIDGE_PORT=8899
BUNDLE_NAME="atmos-deploy-bundle.zip"

FORCE=false
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=true ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--force]" >&2
      exit 2
      ;;
  esac
done

if [[ ! -f "$CREATE_PS1" ]]; then
  echo "Cannot find $CREATE_PS1" >&2
  exit 1
fi

export PATH="$HOME/.dotnet:$PATH"

WORKDIR="$(mktemp -d)"
cleanup() {
  [[ -n "${BRIDGE_PID:-}" ]] && kill "$BRIDGE_PID" >/dev/null 2>&1 || true
  rm -rf "$WORKDIR"
}
trap cleanup EXIT

echo "== Publishing Atmos.Web (Release) =="
dotnet publish "$WEB_PROJECT/Atmos.Web.csproj" -c Release -o "$WORKDIR/bundle/publish"
echo

echo "== Generating the current EF Core migration script =="
(cd "$REPO_ROOT" && dotnet tool restore >/dev/null)
(cd "$REPO_ROOT" && dotnet ef migrations script --idempotent \
  --project src/Atmos.Web --startup-project src/Atmos.Web \
  -o "$WORKDIR/bundle/migrate.sql")
echo

winrm_init

if [[ "$FORCE" != true ]]; then
  echo "== Dry run: asking the VM for its current state (no bundle transferred, no changes) =="
  echo
  winrm_run_ps1 "$CREATE_PS1"
  echo
  echo "(Publish and migration-script generation above both succeeded — that's real signal, not just a VM-side dry run. Re-run with --force to actually deploy.)"
  exit 0
fi

SQL_PASSWORD="${ATMOS_SQL_ATMOS_APP_PASSWORD:-$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 24)Aa1!}"

echo "== Bundling publish output + migrate.sql =="
(cd "$WORKDIR/bundle" && zip -rq "$WORKDIR/$BUNDLE_NAME" publish migrate.sql)
echo "Bundle size: $(du -h "$WORKDIR/$BUNDLE_NAME" | cut -f1)"
echo

echo "== Starting temporary file bridge on ${BRIDGE_IP}:${BRIDGE_PORT} =="
(cd "$WORKDIR" && python3 -m http.server "$BRIDGE_PORT" --bind "$BRIDGE_IP" >/dev/null 2>&1) &
BRIDGE_PID=$!
sleep 1
if ! kill -0 "$BRIDGE_PID" 2>/dev/null; then
  echo "File bridge failed to start on ${BRIDGE_IP}:${BRIDGE_PORT} (already in use?)." >&2
  exit 1
fi
BUNDLE_URL="http://${BRIDGE_IP}:${BRIDGE_PORT}/${BUNDLE_NAME}"
echo "Serving $BUNDLE_URL"
echo

echo "== Running vm-create.ps1 on the VM (FORCE) =="
echo
winrm_run_ps1 "$CREATE_PS1" -Force -BundleUrl "'$BUNDLE_URL'" -SqlPassword "'$SQL_PASSWORD'"
