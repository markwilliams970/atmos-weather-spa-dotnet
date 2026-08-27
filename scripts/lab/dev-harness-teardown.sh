#!/usr/bin/env bash
#
# D17A (DELETE) — tears down the local Docker dev harness described in
# docs/phase-c-build-environment.md: the atmos-sql-dev SQL Server 2022
# container, its atmos-sql-dev-data named volume, and the .NET User Secrets
# entry that points Atmos.Web at it.
#
# Safe to re-run: every step checks whether its target exists before acting,
# so a partial prior cleanup (or a fresh machine that never had the harness)
# doesn't error out.
#
# Defaults to a DRY RUN — it only reports what exists and what would be
# removed. Pass --force to actually delete anything.
#
# Usage:
#   scripts/lab/dev-harness-teardown.sh              # dry run (default)
#   scripts/lab/dev-harness-teardown.sh --force       # actually tear down
#   scripts/lab/dev-harness-teardown.sh --force --with-image
#                                                      # also remove the
#                                                      # mssql/server:2022-latest
#                                                      # image (not just the
#                                                      # container/volume) —
#                                                      # off by default since
#                                                      # it's a large,
#                                                      # slow-to-re-pull
#                                                      # shared base image,
#                                                      # not really this
#                                                      # harness's own state.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTAINER_NAME="atmos-sql-dev"
VOLUME_NAME="atmos-sql-dev-data"
IMAGE_NAME="mcr.microsoft.com/mssql/server:2022-latest"
SECRET_PROJECT="$REPO_ROOT/src/Atmos.Web"
SECRET_KEY="ConnectionStrings:AtmosDb"

FORCE=false
WITH_IMAGE=false
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=true ;;
    --with-image) WITH_IMAGE=true ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--force] [--with-image]" >&2
      exit 2
      ;;
  esac
done

if [[ "$FORCE" != true ]]; then
  echo "== DRY RUN (pass --force to actually delete) =="
fi
echo

container_exists() { docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; }
volume_exists()    { docker volume ls --format '{{.Name}}' | grep -qx "$VOLUME_NAME"; }
image_exists()     { docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; }
secret_exists() {
  dotnet user-secrets list --project "$SECRET_PROJECT" 2>/dev/null | grep -q "^${SECRET_KEY} ="
}

echo "-- Container: $CONTAINER_NAME --"
if container_exists; then
  status="$(docker inspect -f '{{.State.Status}}' "$CONTAINER_NAME")"
  echo "found (status: $status)"
  if [[ "$FORCE" == true ]]; then
    echo "stopping..."
    docker stop "$CONTAINER_NAME" >/dev/null
    echo "removing..."
    docker rm "$CONTAINER_NAME" >/dev/null
    echo "removed."
  else
    echo "would stop + remove."
  fi
else
  echo "not found — nothing to do."
fi
echo

echo "-- Volume: $VOLUME_NAME --"
if volume_exists; then
  echo "found."
  if [[ "$FORCE" == true ]]; then
    docker volume rm "$VOLUME_NAME" >/dev/null
    echo "removed."
  else
    echo "would remove. (This is where the database's actual data files live — removing it discards all local dev data permanently.)"
  fi
else
  echo "not found — nothing to do."
fi
echo

if [[ "$WITH_IMAGE" == true ]]; then
  echo "-- Image: $IMAGE_NAME --"
  if image_exists; then
    echo "found."
    if [[ "$FORCE" == true ]]; then
      docker image rm "$IMAGE_NAME" >/dev/null
      echo "removed."
    else
      echo "would remove."
    fi
  else
    echo "not found — nothing to do."
  fi
  echo
fi

echo "-- .NET User Secrets: $SECRET_KEY (Atmos.Web) --"
if secret_exists; then
  echo "found."
  if [[ "$FORCE" == true ]]; then
    dotnet user-secrets remove "$SECRET_KEY" --project "$SECRET_PROJECT" >/dev/null
    echo "removed."
  else
    echo "would remove. (Stale once the container above is gone — points at a connection string that will no longer resolve.)"
  fi
else
  echo "not found — nothing to do."
fi
echo

if [[ "$FORCE" != true ]]; then
  echo "(Dry run — nothing was actually deleted. Re-run with --force to perform the cleanup above.)"
  exit 0
fi

echo "== Verification =="
verify_ok=true
if container_exists; then echo "FAIL: container $CONTAINER_NAME still present."; verify_ok=false
else echo "OK: container $CONTAINER_NAME absent."; fi
if volume_exists; then echo "FAIL: volume $VOLUME_NAME still present."; verify_ok=false
else echo "OK: volume $VOLUME_NAME absent."; fi
if [[ "$WITH_IMAGE" == true ]]; then
  if image_exists; then echo "FAIL: image $IMAGE_NAME still present."; verify_ok=false
  else echo "OK: image $IMAGE_NAME absent."; fi
fi
if secret_exists; then echo "FAIL: user-secrets entry $SECRET_KEY still present."; verify_ok=false
else echo "OK: user-secrets entry $SECRET_KEY absent."; fi

if [[ "$verify_ok" == true ]]; then
  echo
  echo "Dev harness fully torn down."
else
  echo
  echo "One or more artifacts are still present — see FAIL lines above." >&2
  exit 1
fi
