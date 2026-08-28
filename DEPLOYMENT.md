# Deployment

Short and task-oriented: how to actually get Atmos Weather onto an IIS/SQL Server host. For the *why* behind each step — and to actually learn the IIS/SQL Server mechanics rather than just run a script — see [`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md) instead. This document is the "just deploy it" path; that one is the "understand it" path.

---

## Prerequisites

On the target Windows Server host:

- IIS with the **Web Server** role installed.
- The [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) installed (gives IIS the ASP.NET Core Module / ANCM).
- SQL Server (any edition) with **Mixed Mode authentication** enabled — this app connects via a dedicated SQL login, not Windows-integrated auth, so Windows-only auth mode won't work.

None of the above is installed by this project's own deployment automation — it verifies each is present and fails with a clear message if not, rather than attempting a large, risky silent install. See [`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md) Parts 1.1–1.3 if you need to actually install any of these from scratch.

On the machine you're deploying *from* (this repo's own automation targets a Linux workstation):

- .NET 10 SDK.
- WinRM connectivity to the target host (NTLM auth). On Ubuntu/Mint, NTLM needs OpenSSL's legacy provider enabled (`OPENSSL_CONF`) — `scripts/lab/_winrm-common.sh` handles this automatically; see `docs/phase-c-build-environment.md`'s "Note on WinRM tooling" if connecting some other way.
- A way for the target host to pull a ~30MB published bundle across (this project's automation runs a temporary local HTTP server for the target to fetch from over the network — see `docs/phase-c-build-environment.md`'s "File transfer note").

---

## Deploy (scripted)

```bash
scripts/lab/deploy-to-vm.sh              # dry run — reports current state only
scripts/lab/deploy-to-vm.sh --force       # actually deploys
```

This publishes `Atmos.Web`, generates the current EF Core migration script, and runs `scripts/lab/vm-create.ps1` on the target over WinRM, which:

1. Verifies IIS and the Hosting Bundle are present (installs them if genuinely missing).
2. Verifies SQL Server is reachable and in Mixed Mode; **fails clearly rather than proceeding** if not.
3. Creates (or, if redeploying, resets) the `AtmosDb` database, the dedicated `atmos_app` SQL login/user, and its grants — idempotent either way.
4. Applies the EF Core migration.
5. Deploys the published files to the IIS site's physical path (stopping the site/pool first, since in-process hosting locks the running DLL).
6. Creates the IIS app pool and site if they don't already exist.
7. Sets filesystem permissions for the app pool identity.
8. Rewrites `web.config`: `ASPNETCORE_ENVIRONMENT=Production` and the `ConnectionStrings__AtmosDb` secret in `<environmentVariables>`, plus `<remove name="WebDAVModule" />`/`<remove name="WebDAV" />` — **required if the IIS box has the WebDAV Publishing role service installed** (common under a default "Common HTTP Features" install): IIS's WebDAV module otherwise intercepts `PUT`/`DELETE` at the module level before your own handlers ever see them, silently breaking `PUT /api/recent/units` with IIS's own generic 405 page rather than anything from the app. **All of this matters on every redeploy**, not just the first one: `dotnet publish` regenerates `web.config` from scratch every time, so none of it survives on its own — this script re-adds it automatically, but if you ever do a manual `dotnet publish` + copy instead, you have to redo it by hand (see Part 4.5 of the manual walkthrough, and its WebDAV note).
9. Creates the structured-log directory and the inbound firewall rule for the site's port.
10. Starts the app pool and site, then **actually verifies it**: query-based checks that the database/login/schema exist, and a real `GET /healthz` request through IIS → Kestrel → SQL Server.

Environment variables `ATMOS_VM_HOST` / `ATMOS_VM_USER` / `ATMOS_VM_PASSWORD` override the deployment target (defaults point at this project's own lab VM).

## Tear down (scripted)

```bash
scripts/lab/run-vm-teardown.sh              # dry run — reports what would be removed
scripts/lab/run-vm-teardown.sh --force       # actually removes it
```

Stops and removes the IIS site/app pool, deletes the published files, **drops** (not truncates) the `AtmosDb` database and the `atmos_app` login, removes the firewall rule and the log directory — with an explicit before/after query-based validation report printed at the end. IIS and SQL Server *themselves* are never touched; only the app-specific pieces.

## Deploy or tear down manually

Follow [`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md) start to finish — it covers the same ground as the scripts above, one command at a time, with the reasoning for each step and a troubleshooting table for the errors you're most likely to actually hit (`HTTP 503`, a missing `web.config` connection string, a permissions failure writing logs, etc.).

---

## Current state

This section goes stale the moment either environment is created or torn down again — treat it as a hint, not a fact, and check for real (`curl http://192.168.122.54:8080/healthz`, `docker ps -a --filter name=atmos-sql-dev`) before relying on it. See `docs/phase-c-build-environment.md`'s "Current state" section for the fuller, more frequently updated version of this.

As of the last update to this file: the lab VM deployment is **up** (redeployed and verified as part of exercising this tooling for real); the local Docker dev harness is **torn down** (most recently exercised end-to-end — create, use, delete — and left down afterward). `deploy-to-vm.sh --force` / `dev-harness-create.sh` bring either back; `run-vm-teardown.sh --force` / `dev-harness-teardown.sh --force` tear either down.

---

## Troubleshooting

For anything beyond "the script's own error message already told me what's wrong," see `docs/manual-deployment-walkthrough.md`'s Part 6 — a table of the specific HTTP 503 / 500.30 / 502.5 failure modes this app is likely to hit under IIS, what each actually means, and where to look.
