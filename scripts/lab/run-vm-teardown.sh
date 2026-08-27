#!/usr/bin/env bash
#
# Runs scripts/lab/vm-teardown.ps1 against the win2025app lab VM over WinRM,
# from this Linux dev machine — the same connection mechanism (pywinrm,
# NTLM, the OpenSSL "legacy" provider workaround for MD4) used throughout
# this project's own VM automation; see docs/phase-c-build-environment.md's
# "Note on WinRM tooling".
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

VM_HOST="${ATMOS_VM_HOST:-192.168.122.54}"
VM_USER="${ATMOS_VM_USER:-Administrator}"
VM_PASSWORD="${ATMOS_VM_PASSWORD:-TestP@ssw0rd123}"

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

if ! python3 -c "import winrm" >/dev/null 2>&1; then
  echo "pywinrm is not installed for python3. Install it with:" >&2
  echo "  pip install --user pywinrm" >&2
  exit 1
fi

# Ubuntu/Mint's OpenSSL 3 disables the legacy provider (MD4) by default,
# which NTLM auth needs. This enables it for this process only, without
# touching the system-wide OpenSSL config — same fix documented in
# docs/phase-c-build-environment.md.
OPENSSL_LEGACY_CONF="$(mktemp)"
cat > "$OPENSSL_LEGACY_CONF" <<'EOF'
openssl_conf = openssl_init

[openssl_init]
providers = provider_sect

[provider_sect]
default = default_sect
legacy = legacy_sect

[default_sect]
activate = 1

[legacy_sect]
activate = 1
EOF

echo "Running vm-teardown.ps1 against $VM_HOST as $VM_USER ($( [[ -n "$FORCE_FLAG" ]] && echo "FORCE" || echo "dry run" ))..."
echo

# The Python "runner" has to live in its own file, not a heredoc on the same
# command as the piped PowerShell content below — a heredoc and a piped
# stdin can't both feed the same command; the heredoc wins and the pipe's
# data is silently discarded, which previously left the WinRM call with an
# empty script body.
PY_RUNNER="$(mktemp)"
trap 'rm -f "$OPENSSL_LEGACY_CONF" "$PY_RUNNER"' EXIT
cat > "$PY_RUNNER" <<'PYEOF'
import sys
import winrm

host, user, password = sys.argv[1], sys.argv[2], sys.argv[3]
script = sys.stdin.read()

session = winrm.Session(host, auth=(user, password), transport='ntlm')
result = session.run_ps(script)

sys.stdout.write(result.std_out.decode(errors='replace'))
err = result.std_err.decode(errors='replace').strip()
if err:
    sys.stderr.write(err + "\n")

# Always exit 0 here regardless of result.status_code: PowerShell's own
# process exit code over WinRM is unreliable for judging success — a brand
# new remote session's one-time "Preparing modules for first use" progress
# record alone was observed setting status_code=1 for an otherwise-successful
# no-op command. Judge success from the printed output (vm-teardown.ps1
# prints its own explicit PASS/FAIL table), not this transport-level code —
# the same approach this project's earlier WinRM tooling already used.
sys.exit(0)
PYEOF

run_remote() {
  OPENSSL_CONF="$OPENSSL_LEGACY_CONF" python3 "$PY_RUNNER" "$VM_HOST" "$VM_USER" "$VM_PASSWORD"
}

# vm-teardown.ps1 (~10KB, well-commented on purpose — it's meant to be read
# directly on the VM too) is too big to send as a single WinRM -EncodedCommand
# call — WinRS's underlying cmd.exe-style remote shell caps the command line
# at roughly 8191 characters, and pywinrm's run_ps re-encodes the script as
# UTF-16LE + base64 (~2.7x inflation) before sending it, so anything much
# past ~3000 source characters trips "The command line is too long." Transfer
# it in small base64 chunks instead — the same shape as apply_migration.ps1's
# earlier WriteAllBytes(FromBase64String(...)) approach, just split across
# several WinRM round trips so each individual call stays comfortably small.
REMOTE_B64_PATH='C:\Windows\Temp\atmos-vm-teardown.ps1.b64'
REMOTE_PS1_PATH='C:\Windows\Temp\atmos-vm-teardown.ps1'
CHUNK_SIZE=2000

echo "Uploading vm-teardown.ps1 to the VM..."
echo "Remove-Item -Path '$REMOTE_B64_PATH' -ErrorAction SilentlyContinue" | run_remote >/dev/null

B64="$(base64 -w0 "$TEARDOWN_PS1")"
total=${#B64}
offset=0
chunk_count=0
while [[ $offset -lt $total ]]; do
  chunk="${B64:$offset:$CHUNK_SIZE}"
  # -Encoding ascii keeps this exactly 1 byte per base64 character (no BOM,
  # no CRLF translation) so the byte-length verification below is exact.
  chunk_out="$(echo "Add-Content -Path '$REMOTE_B64_PATH' -Value '$chunk' -NoNewline -Encoding ascii" | run_remote)"
  # Add-Content prints nothing on success — any stdout here means the chunk
  # upload itself failed (e.g. "The command line is too long"), which would
  # otherwise silently corrupt the reassembled script on the VM.
  if [[ -n "$chunk_out" ]]; then
    echo "Chunk upload failed (offset $offset):" >&2
    echo "$chunk_out" >&2
    exit 1
  fi
  offset=$((offset + CHUNK_SIZE))
  chunk_count=$((chunk_count + 1))
done

uploaded_size="$(echo "(Get-Item '$REMOTE_B64_PATH').Length" | run_remote | tr -d '[:space:]')"
if [[ "$uploaded_size" != "$total" ]]; then
  echo "Uploaded file size mismatch: expected $total bytes, VM reports $uploaded_size bytes." >&2
  exit 1
fi
echo "Uploaded in $chunk_count chunk(s), verified $uploaded_size bytes on the VM."
echo

{
  echo "\$__b64 = Get-Content -Path '$REMOTE_B64_PATH' -Raw"
  echo "\$__bytes = [Convert]::FromBase64String(\$__b64)"
  # vm-teardown.ps1 is UTF-8 (its comments use em dashes) with no BOM, as
  # written on this Linux dev machine. Writing the decoded bytes verbatim
  # left Windows PowerShell 5.1 guessing the file's encoding when it later
  # parsed it, misreading the em dashes and corrupting the script ("string
  # missing terminator" a hundred-plus lines past the actual multi-byte
  # character). Decoding to a .NET string and re-writing with Set-Content
  # -Encoding UTF8 (which adds a BOM) makes the encoding explicit instead.
  echo "\$__text = [System.Text.Encoding]::UTF8.GetString(\$__bytes)"
  echo "Set-Content -Path '$REMOTE_PS1_PATH' -Value \$__text -Encoding UTF8"
  echo "Remove-Item -Path '$REMOTE_B64_PATH' -ErrorAction SilentlyContinue"
  echo "& '$REMOTE_PS1_PATH' $FORCE_FLAG"
  echo "Remove-Item -Path '$REMOTE_PS1_PATH' -ErrorAction SilentlyContinue"
} | run_remote
