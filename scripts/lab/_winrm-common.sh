#!/usr/bin/env bash
#
# Shared WinRM helpers for scripts/lab/*.sh — connects to the win2025app lab
# VM (NTLM, with the OpenSSL "legacy" provider workaround for MD4 that
# Ubuntu/Mint's OpenSSL 3 disables by default; see
# docs/phase-c-build-environment.md's "Note on WinRM tooling") and runs a
# local .ps1 file on it, working around WinRS's ~8KB remote command-line
# ceiling by transferring the script in small base64 chunks rather than as a
# single -EncodedCommand call.
#
# Not meant to be run directly — source it from another script:
#   source "$(dirname "${BASH_SOURCE[0]}")/_winrm-common.sh"
#   winrm_init                                      # once, near the top
#   winrm_run_ps1 my-script.ps1 -Force -SomeArg 'value'
#
# ATMOS_VM_HOST / ATMOS_VM_USER / ATMOS_VM_PASSWORD override the connection
# details; defaults are this lab's known, intentionally non-secret values
# (see CLAUDE.md/project notes).

: "${ATMOS_VM_HOST:=192.168.122.54}"
: "${ATMOS_VM_USER:=Administrator}"
: "${ATMOS_VM_PASSWORD:=TestP@ssw0rd123}"

winrm_init() {
  if ! python3 -c "import winrm" >/dev/null 2>&1; then
    echo "pywinrm is not installed for python3. Install it with:" >&2
    echo "  pip install --user pywinrm" >&2
    exit 1
  fi

  _WINRM_OPENSSL_CONF="$(mktemp)"
  cat > "$_WINRM_OPENSSL_CONF" <<'EOF'
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

  _WINRM_PY_RUNNER="$(mktemp)"
  cat > "$_WINRM_PY_RUNNER" <<'PYEOF'
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

# Always exit 0: PowerShell's own process exit code over WinRM is unreliable
# for judging success here — a brand new remote session's one-time
# "Preparing modules for first use" progress record alone was observed
# setting status_code=1 for an otherwise-successful no-op command. Judge
# success from the printed output instead (every scripts/lab/*.ps1 prints
# its own explicit PASS/FAIL report for exactly this reason).
sys.exit(0)
PYEOF

}

# winrm_cleanup -- removes the temp files winrm_init created. Deliberately
# NOT registered as its own `trap ... EXIT` here: bash EXIT traps don't
# chain — a second `trap ... EXIT` call silently *replaces* the first rather
# than adding to it. A caller script that sets its own trap before calling
# winrm_init (deploy-to-vm.sh does, to also kill its file-bridge server and
# clean up its workdir) would have that trap silently clobbered, and its own
# cleanup would simply never run — confirmed as a real, reproducible bug
# this way: a leaked python3 http.server process holding a port open after
# every run. Callers must call winrm_cleanup themselves from their own single
# EXIT trap (see run-vm-teardown.sh and deploy-to-vm.sh for the two shapes
# this takes: a script with nothing else to clean up vs. one that does).
winrm_cleanup() {
  rm -f "$_WINRM_OPENSSL_CONF" "$_WINRM_PY_RUNNER"
}

# winrm_exec  -- reads a PowerShell script from stdin, runs it on the VM,
# prints its output.
winrm_exec() {
  OPENSSL_CONF="$_WINRM_OPENSSL_CONF" python3 "$_WINRM_PY_RUNNER" "$ATMOS_VM_HOST" "$ATMOS_VM_USER" "$ATMOS_VM_PASSWORD"
}

# winrm_upload_file <local_path> <remote_path>
# Transfers a small-to-medium text file (a .ps1 script, not a large binary
# bundle — use the HTTP bridge pattern in deploy-to-vm.sh for those) to the
# VM in base64 chunks, then reassembles it there with an explicit UTF-8
# re-encode. That re-encode matters: writing the decoded bytes verbatim
# leaves Windows PowerShell 5.1 guessing the file's encoding, and it
# misreads non-ASCII characters (em dashes in these scripts' own comments),
# corrupting the parse.
winrm_upload_file() {
  local local_path="$1" remote_path="$2" remote_b64_path="${2}.b64"
  local chunk_size=2000

  echo "Remove-Item -Path '$remote_b64_path' -ErrorAction SilentlyContinue" | winrm_exec >/dev/null

  local b64 total offset chunk chunk_out
  b64="$(base64 -w0 "$local_path")"
  total=${#b64}
  offset=0
  while [[ $offset -lt $total ]]; do
    chunk="${b64:$offset:$chunk_size}"
    # Add-Content prints nothing on success — any stdout here means the
    # chunk upload itself failed (e.g. "The command line is too long"),
    # which would otherwise silently corrupt the reassembled file.
    chunk_out="$(echo "Add-Content -Path '$remote_b64_path' -Value '$chunk' -NoNewline -Encoding ascii" | winrm_exec)"
    if [[ -n "$chunk_out" ]]; then
      echo "Chunk upload failed (offset $offset) uploading $local_path:" >&2
      echo "$chunk_out" >&2
      return 1
    fi
    offset=$((offset + chunk_size))
  done

  local uploaded_size
  uploaded_size="$(echo "(Get-Item '$remote_b64_path').Length" | winrm_exec | tr -d '[:space:]')"
  if [[ "$uploaded_size" != "$total" ]]; then
    echo "Uploaded file size mismatch for $local_path: expected $total bytes, VM reports $uploaded_size bytes." >&2
    return 1
  fi

  {
    echo "\$__b64 = Get-Content -Path '$remote_b64_path' -Raw"
    echo "\$__bytes = [Convert]::FromBase64String(\$__b64)"
    echo "\$__text = [System.Text.Encoding]::UTF8.GetString(\$__bytes)"
    echo "Set-Content -Path '$remote_path' -Value \$__text -Encoding UTF8"
    echo "Remove-Item -Path '$remote_b64_path' -ErrorAction SilentlyContinue"
  } | winrm_exec >/dev/null
}

# winrm_run_ps1 <local_ps1_path> [extra invocation args...]
# Uploads the script, invokes it with the given trailing arguments (passed
# through to the script's own param() block), then removes it from the VM.
# Extra args are inserted verbatim into the generated PowerShell invocation
# line — only pass values you control (this project's own scripts, with
# generated passwords/URLs that are plain alphanumeric/URL-safe), not
# arbitrary user input.
winrm_run_ps1() {
  local local_path="$1"; shift
  local remote_path="C:\\Windows\\Temp\\$(basename "$local_path")"
  local extra_args="$*"

  winrm_upload_file "$local_path" "$remote_path" || return 1

  {
    echo "& '$remote_path' $extra_args"
    echo "Remove-Item -Path '$remote_path' -ErrorAction SilentlyContinue"
  } | winrm_exec
}
