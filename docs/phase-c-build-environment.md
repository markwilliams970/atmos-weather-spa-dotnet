# Phase C — Build Environment

**Status:** Complete. Toolchain and deployment environment established and validated end-to-end, including the Stage C+ deployment rehearsal from `docs/phase-b-target-architecture.md` §17/§19.

This phase produced the actual solution/project skeleton (D1 territory per CLAUDE.md's implementation sequence) because Stage C+ requires a real, deployable `Atmos.Web` rather than a disposable throwaway — see Phase B §19. No forecast features were built; this is deliberately still a minimal skeleton (default Razor Pages template + `RecentSearch`/`AtmosDbContext` + a health check). Feature work starts in Phase D.

**See also:** [`docs/manual-deployment-walkthrough.md`](./manual-deployment-walkthrough.md) covers the same IIS/SQL Server/app-pool ground as a didactic, step-by-step guide for deploying to a fresh VM by hand — useful both as a learning companion to the automated steps below and as a way to sanity-check this document against a from-scratch deployment.

---

## Current state (updated 2026-08-28 — read this before trusting anything below as "what's running")

Everything in this document past this point is a **historical record of Phase C's original, one-time setup** — accurate as a description of what was done and why, not a live status page. Since then, the lab environment described here has been fully formalized into idempotent scripts (`scripts/lab/dev-harness-create.sh`/`-teardown.sh` for the Docker dev harness, `scripts/lab/deploy-to-vm.sh` + `scripts/lab/vm-create.ps1`/`vm-teardown.ps1` for the VM deployment — see `docs/manual-deployment-walkthrough.md`'s appendix), and the **full create/delete lifecycle for both has now been exercised for real** (not just dry-run) — see Phase E below and each script's own commit history for what that surfaced and fixed.

**As of this writing:**

- **`win2025app` (the VM) is deployed and healthy** — `AtmosWeb` site/app pool running, `AtmosDb` database and `atmos_app` login present, `GET /healthz` returns `Healthy`. Redeploy with `scripts/lab/deploy-to-vm.sh --force` any time (idempotent); tear it down with `scripts/lab/run-vm-teardown.sh --force`.
- **The local Docker dev harness is torn down.** Bring it back with `scripts/lab/dev-harness-create.sh`.

Don't assume either state stays this way for long — both are routinely created and torn down as part of exercising this tooling. Check for real (`curl http://192.168.122.54:8080/healthz`, `docker ps -a --filter name=atmos-sql-dev`) rather than trusting this paragraph if it's been a while.

## Phase E — what redeploying for real actually found

Every one of vm-create.ps1's "already exists" idempotent redeploys had, by definition, never exercised its own from-scratch code paths — the VM already had everything from the original Phase C setup. Tearing the VM fully down and redeploying from nothing for Phase E (CLAUDE.md §23's checklist) ran those paths for the first time and surfaced four real, previously-undetected bugs, all fixed and re-verified via a subsequent clean redeploy:

1. **The EF Core migration was silently applied to `master`, not `AtmosDb`.** `sqlcmd -S localhost -E -i migrate.sql` had no `-d` flag — `sqlcmd` with no `-d` connects using the admin login's own default database, and the idempotent migration script has no `USE` statement of its own. `sqlcmd` exits 0 and prints success regardless, so nothing *looked* wrong; only a query-based schema check inside `AtmosDb` specifically caught it. **The lesson generalizes past this project:** any `sqlcmd -E -i script.sql` invocation without an explicit `-d <database>` is trusting the connecting login's default database, silently — always pass `-d` unless that's genuinely what you want.
2. **IIS start/stop timing races.** `Stop-WebAppPool`/`Stop-Website` returning doesn't mean `w3wp.exe` has actually released its file locks yet, and IIS's Windows Process Activation Service can transiently refuse a `Start-WebAppPool`/`Start-Website` call immediately after a `Stop` ("The service cannot accept control messages at this time"). Both are timing races IIS itself doesn't give you a clean "wait until ready" signal for — retrying with a short backoff is the practical fix (see `vm-create.ps1`'s `Invoke-WithRetry`).
3. **The file-transfer bridge process leaked on every deploy** — a Linux/bash-side bug, not IIS: backgrounding `(cd "$WORKDIR" && python3 -m http.server ...) &` captures the *subshell's* PID, not python3's, so killing it left python3 running as an orphan; separately, a shared helper script's own `trap ... EXIT` silently clobbered the caller's trap (bash `EXIT` traps don't chain — a second one replaces the first). See `scripts/lab/_winrm-common.sh`'s `winrm_cleanup` comment for the fix.
4. **IIS's WebDAV module silently blocked the app's one mutating endpoint, `PUT /api/recent/units`, on every deployment this project has ever done.** This is the one worth knowing even if you never touch this repo's scripts again: if an IIS box has the WebDAV Publishing role service installed (common under a default "Common HTTP Features" install), IIS's `WebDAVModule` intercepts `PUT`/`DELETE`/`PROPFIND`/etc. **at the module level, before handler selection even happens** — regardless of your own `aspNetCore` handler's `verb="*"`. The symptom is IIS's own generic 405 page, not anything from your app, which makes it easy to misdiagnose as an application bug. The fix is a `web.config` addition scoped to just that site:
   ```xml
   <system.webServer>
     <modules>
       <remove name="WebDAVModule" />
     </modules>
     <handlers>
       <remove name="WebDAV" />
       <!-- ... your existing handlers ... -->
     </handlers>
   </system.webServer>
   ```
   `vm-create.ps1` now injects exactly this on every deploy (`web.config` gets regenerated from scratch by `dotnet publish` each time, same reason the environment-variables step has to re-run too).

A fifth bug, not IIS/SQL-specific, turned up running the same checklist's mobile-layout check: the mobile bottom nav had been **completely invisible at every viewport size** since it was built, due to a CSS specificity bug (an ID selector silently outranking the class selector meant to show it — see `site.css`'s comment at `.bottom-nav`). Worth knowing mainly as a reminder that "looks fine in the one viewport you always test at" and "actually works" are different claims.

---

## Environment inventory

**Local development workstation** (Linux Mint 22.3, this machine):
- .NET SDK 10.0.400 — installed user-local (`~/.dotnet`), no root required, since passwordless `sudo` isn't configured here. Added to `PATH`/`DOTNET_ROOT` via `~/.bashrc`.
- Docker 29.7.2 — daemon required a one-time `sudo systemctl start docker` (not enabled at boot on this machine).
- `dotnet-ef` 10.0.11 — installed as a local tool (`dotnet-tools.json`, restored via `dotnet tool restore`).

**Local dev SQL Server** — SQL Server 2022 Developer Edition in Docker (`mcr.microsoft.com/mssql/server:2022-latest`), container `atmos-sql-dev`, port 1433, data in a named volume (`atmos-sql-dev-data`) so it survives container restarts. This is the fast-iteration database Phase B §16/CLAUDE.md §20 call for — not the deployment target.

This was originally stood up by hand with:
```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<...>" \
  -p 1433:1433 --name atmos-sql-dev --restart unless-stopped \
  -v atmos-sql-dev-data:/var/opt/mssql \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

That one-off command is now formalized as **`scripts/lab/dev-harness-create.sh`** — idempotent (safe to re-run; leaves an already-running container alone), generates its own random `sa` password (or accepts `ATMOS_DEV_SQL_SA_PASSWORD`), waits for SQL Server to actually be ready before continuing, writes the connection string straight to User Secrets, and applies the current EF Core migration (`dotnet ef database update`) so `AtmosDb` exists with the right schema by the time it returns. `scripts/lab/dev-harness-teardown.sh` is the reverse — see that script's own comments, and `docs/manual-deployment-walkthrough.md`'s appendix, for both.

The `Atmos.Web` connection string for this container is stored via **.NET User Secrets** (`dotnet user-secrets set "ConnectionStrings:AtmosDb" "..." --project src/Atmos.Web`), not in `appsettings.Development.json` — keeps the credential out of source control entirely, per CLAUDE.md §13.

**Deployment target VM** — `win2025app`, a QEMU/libvirt VM on the `virbr0` NAT network (`192.168.122.54`), reachable via WinRM (port 5985, NTLM auth). Confirmed state:

| Component | Found | Action taken |
|---|---|---|
| OS | Windows Server 2025 Standard Evaluation, build 26100 | none needed |
| IIS | Already installed (Web-Server, ASP.NET 4.8, Management Console) | none needed |
| SQL Server | Already installed — **SQL Server 2022 (RTM) Developer Edition**, default instance `MSSQLSERVER`, Mixed Mode auth enabled, TCP/IP on static port 1433 | none needed for the engine itself |
| .NET / ASP.NET Core Hosting Bundle | Not present | Installed .NET 10.0.0 ASP.NET Core Hosting Bundle; confirmed `AspNetCoreModuleV2` registered in IIS after `iisreset` |

**Note on WinRM tooling:** connecting from this Linux machine via `pywinrm`'s NTLM transport initially failed with `unsupported hash type md4` — Ubuntu/Mint's OpenSSL 3 disables the legacy provider (MD4, needed for NTLMv2) by default. Fixed with a scratch `OPENSSL_CONF` enabling the `legacy` provider for the Python process, without touching the system-wide OpenSSL config. Documented here so a future session doesn't have to rediscover this.

---

## SQL Server — dedicated least-privilege login (VM)

Rather than deploying with `sa`, created a dedicated SQL login on the VM's SQL Server:

- Login: `atmos_app` (SQL authentication, password policy enforced)
- Database: `AtmosDb`
- Grants: `db_datareader`, `db_datawriter`, plus `CREATE TABLE` / `ALTER ON SCHEMA::dbo` (sufficient to both apply migrations and run the app under one identity for this rehearsal)

**Superseded:** at the time this was written, the password lived in a server-only `appsettings.Production.json`. A later Phase D logging pass moved it to `web.config`'s `<aspNetCore><environmentVariables>` as `ConnectionStrings__AtmosDb` instead — see `docs/logging.md`'s "Deployment note" for why, and `scripts/lab/vm-create.ps1` for the current, idempotent version of this whole login/user/grants setup (it resets the login's password on every run rather than assuming a fixed one, so there's no single "the password" to record anywhere, including here). Phase D/E should still decide whether migrations continue to run under `atmos_app` or a separate, more-privileged deployment-only login — a minor tightening, not a blocker.

---

## What was proven

1. **.NET SDK** — `dotnet build`/`dotnet test` succeed across all 5 projects on this machine.
2. **Project scaffolding** — `Atmos.sln` (`.slnx`, .NET 10's new default solution format) with `Atmos.Core`, `Atmos.Web`, `Atmos.Cli`, `Atmos.Core.Tests`, `Atmos.Web.Tests`, wired with the project references specified in Phase B §2.
3. **SQL Server connectivity** — both the local Docker instance and the VM's real SQL Server accept connections and EF Core migrations.
4. **EF Core tooling** — `dotnet ef migrations add InitialCreate` generated a migration matching the Phase B §7 schema exactly (verified column types, both indexes including the descending/covering `IX_RecentSearch_SessionId_LastAccessedUtc`, both check constraints). Applied via `dotnet ef database update` locally, and via `dotnet ef migrations script --idempotent` executed through `sqlcmd` on the VM (the migration-bundle/script approach CLAUDE.md §22 and Phase B §17 specify for environments without the SDK).
5. **Test execution** — `dotnet test` passes for `Atmos.Core.Tests` and `Atmos.Web.Tests` (template placeholder tests at this stage; real tests start in Phase D).
6. **IIS deployment prerequisites (Stage C+)** — `Atmos.Web` published Release/framework-dependent, deployed to a dedicated IIS site (`AtmosWeb`, its own app pool `AtmosWebPool`, "No Managed Code"), on port 8080 (the VM's Default Web Site on port 80 was left untouched). `ASPNETCORE_ENVIRONMENT=Production` set via `web.config`'s `<aspNetCore><environmentVariables>` (the correct mechanism for the in-process hosting model — the app-pool-level `processModel.environmentVariables` PowerShell property doesn't exist on this IIS version's provider). Verified:
   - `GET http://192.168.122.54:8080/` → 200 (from both the VM itself and this workstation, confirming the new firewall rule for port 8080 works)
   - `GET http://192.168.122.54:8080/healthz` → `Healthy` — a `Microsoft.Extensions.Diagnostics.HealthChecks` endpoint added specifically to prove the **deployed, IIS-hosted app** can reach the VM's real SQL Server through `atmos_app`, not just that migrations could be applied out-of-band via `sqlcmd`.

   This was this project's *first* proof this worked at all. A more complete, repeatable version of the same proof now exists as `scripts/lab/vm-create.ps1`'s own end-of-run verification report (query-based checks for the database/login/schema *and* the same live `/healthz` request) — see the "Phase E" section above for what running that for real actually found.

---

## Deliberately deferred (per Phase B §17)

- **HTTPS binding** — Stage C+ proves IIS/hosting-bundle/SQL Server mechanics over plain HTTP on port 8080. Certificate provisioning and HTTPS binding are Phase E deployment-checklist items (CLAUDE.md §23), not part of this rehearsal.
- **Migration-login tightening** — `atmos_app` currently has both runtime and schema-change rights; splitting these is a small Phase E hardening step, not a Phase C blocker.

---

## File transfer note

Deploying to the VM required moving a ~32MB published build across — too large for WinRM's inline command channel. Used a temporary local HTTP server (`python3 -m http.server`, bound to the `virbr0` bridge IP `192.168.122.1`) for the VM to pull from via `Invoke-WebRequest`, which needed one local firewall rule (`ufw allow from 192.168.122.0/24 to any port 8899 proto tcp`) opened for the lab subnet only. The temporary server was stopped after use; the firewall rule was left in place since it will be needed again for the next deploy in Phase D/E — narrow scope (lab-only subnet, single port), low risk, but worth knowing it's there.

This exact approach — same bridge IP, same port 8899, same rule — is what `scripts/lab/deploy-to-vm.sh` automates: it starts and stops the bridge server itself around each deploy, but still depends on that `ufw` rule already existing on this workstation. If it's ever missing (a fresh dev machine, or the rule got removed), `deploy-to-vm.sh --force` will fail at the bundle-download step with a `WebException`/connection-refused on the VM side, not an obvious message pointing at the firewall — worth remembering if that happens.
