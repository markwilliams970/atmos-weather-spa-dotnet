#!/usr/bin/env bash
#
# CREATE counterpart to dev-harness-teardown.sh — stands up the local Docker
# dev harness described in docs/phase-c-build-environment.md: SQL Server
# 2022 in a container (atmos-sql-dev, port 1433, data in the atmos-sql-dev-data
# named volume), the AtmosDb database with the current EF Core migration
# applied, and the .NET User Secrets entry that points Atmos.Web at it.
#
# This previously only existed as a one-off `docker run` command pasted into
# docs/phase-c-build-environment.md and run by hand — this script is that
# command made real, reusable, and idempotent, plus the two steps that used
# to follow it manually (waiting for SQL Server to actually be ready, and
# applying the migration).
#
# Idempotent by default: if the container already exists, it's left alone
# (started if stopped) rather than recreated — re-running this script after
# it already succeeded is a safe no-op except for re-applying migrations
# (itself idempotent) and re-writing the same secret.
#
# Usage:
#   scripts/lab/dev-harness-create.sh
#   scripts/lab/dev-harness-create.sh --recreate
#       # destroys the existing container/volume/secret first (delegates to
#       # dev-harness-teardown.sh --force), then creates a fresh one — for
#       # when you actually want to discard local dev data, not just ensure
#       # the harness exists.
#
# The SQL Server `sa` password is randomly generated per run unless
# overridden:
#   ATMOS_DEV_SQL_SA_PASSWORD=... scripts/lab/dev-harness-create.sh
# It's written straight to .NET User Secrets (never to a file in the repo),
# matching how this has always been kept out of source control
# (CLAUDE.md §13) — there's no need to remember or copy it by hand.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTAINER_NAME="atmos-sql-dev"
VOLUME_NAME="atmos-sql-dev-data"
IMAGE_NAME="mcr.microsoft.com/mssql/server:2022-latest"
WEB_PROJECT="$REPO_ROOT/src/Atmos.Web"
SECRET_KEY="ConnectionStrings:AtmosDb"
SQL_PORT=1433

RECREATE=false
for arg in "$@"; do
  case "$arg" in
    --recreate) RECREATE=true ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--recreate]" >&2
      exit 2
      ;;
  esac
done

container_exists() { docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; }
container_running() { docker ps --format '{{.Names}}' | grep -qx "$CONTAINER_NAME"; }

if ! docker info >/dev/null 2>&1; then
  echo "Docker daemon isn't reachable. On this machine it's not enabled at boot:" >&2
  echo "  sudo systemctl start docker" >&2
  exit 1
fi

if [[ "$RECREATE" == true ]]; then
  echo "== --recreate: tearing down the existing harness first =="
  "$REPO_ROOT/scripts/lab/dev-harness-teardown.sh" --force
  echo
fi

echo "== Container: $CONTAINER_NAME =="
if container_exists; then
  if container_running; then
    echo "Already running — leaving it alone. (Use --recreate to discard and rebuild it.)"
  else
    echo "Exists but stopped — starting it."
    docker start "$CONTAINER_NAME" >/dev/null
  fi
  # We don't know this container's sa password unless it was just created
  # below — for an already-existing container, reuse whatever's already in
  # User Secrets (this script only ever wrote it via dotnet user-secrets,
  # never to a file, so that's the one place it can still be read back from).
  SA_PASSWORD="$(dotnet user-secrets list --project "$WEB_PROJECT" 2>/dev/null \
    | sed -n "s/^${SECRET_KEY} = .*Password=\([^;]*\);.*/\1/p")"
  if [[ -z "$SA_PASSWORD" ]]; then
    echo "Container already exists but no matching User Secrets entry was found to recover its password from." >&2
    echo "Re-run with --recreate to rebuild it from scratch with a fresh, known password." >&2
    exit 1
  fi
else
  SA_PASSWORD="${ATMOS_DEV_SQL_SA_PASSWORD:-$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 24)Aa1!}"

  echo "Creating volume: $VOLUME_NAME"
  docker volume create "$VOLUME_NAME" >/dev/null

  echo "Starting container: $CONTAINER_NAME"
  docker run \
    -e "ACCEPT_EULA=Y" \
    -e "MSSQL_SA_PASSWORD=$SA_PASSWORD" \
    -p "${SQL_PORT}:1433" \
    --name "$CONTAINER_NAME" \
    --restart unless-stopped \
    -v "${VOLUME_NAME}:/var/opt/mssql" \
    -d "$IMAGE_NAME" >/dev/null
fi
echo

echo "== Waiting for SQL Server to accept connections =="
ready=false
for attempt in $(seq 1 30); do
  if docker exec "$CONTAINER_NAME" /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  echo "SQL Server didn't become ready after 60s. Check 'docker logs $CONTAINER_NAME'." >&2
  exit 1
fi
echo "Ready (after $((attempt * 2))s)."
echo

echo "== .NET User Secrets =="
CONNECTION_STRING="Server=localhost,${SQL_PORT};Database=AtmosDb;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True"
dotnet user-secrets set "$SECRET_KEY" "$CONNECTION_STRING" --project "$WEB_PROJECT" >/dev/null
echo "Set $SECRET_KEY for $WEB_PROJECT."
echo

echo "== Applying EF Core migrations (creates AtmosDb if it doesn't exist) =="
(cd "$REPO_ROOT" && dotnet tool restore >/dev/null)
(cd "$REPO_ROOT" && dotnet ef database update --project src/Atmos.Web --startup-project src/Atmos.Web)
echo

echo "== Verification =="
tables="$(docker exec "$CONTAINER_NAME" /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" -C -d AtmosDb -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME")"
echo "AtmosDb tables:"
echo "$tables"

verify_ok=true
if ! echo "$tables" | grep -qx "RecentSearch"; then
  echo "FAIL: RecentSearch table not found."
  verify_ok=false
fi
if ! echo "$tables" | grep -qx "__EFMigrationsHistory"; then
  echo "FAIL: __EFMigrationsHistory table not found."
  verify_ok=false
fi

echo
if [[ "$verify_ok" == true ]]; then
  echo "Dev harness ready: $CONTAINER_NAME on localhost:$SQL_PORT, AtmosDb schema applied, secrets configured."
else
  echo "Harness came up but schema verification failed — see FAIL lines above." >&2
  exit 1
fi
