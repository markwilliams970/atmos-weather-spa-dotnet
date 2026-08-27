# Phase C — Build Environment

**Status:** Complete. Toolchain and deployment environment established and validated end-to-end, including the Stage C+ deployment rehearsal from `docs/phase-b-target-architecture.md` §17/§19.

This phase produced the actual solution/project skeleton (D1 territory per CLAUDE.md's implementation sequence) because Stage C+ requires a real, deployable `Atmos.Web` rather than a disposable throwaway — see Phase B §19. No forecast features were built; this is deliberately still a minimal skeleton (default Razor Pages template + `RecentSearch`/`AtmosDbContext` + a health check). Feature work starts in Phase D.

**See also:** [`docs/manual-deployment-walkthrough.md`](./manual-deployment-walkthrough.md) covers the same IIS/SQL Server/app-pool ground as a didactic, step-by-step guide for deploying to a fresh VM by hand — useful both as a learning companion to the automated steps below and as a way to sanity-check this document against a from-scratch deployment.

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

The password is stored only in `C:\inetpub\AtmosWeb\appsettings.Production.json` on the VM (not in source control, not reproduced in this document). Phase D/E should decide whether migrations continue to run under `atmos_app` or a separate, more-privileged deployment-only login — a minor tightening, not a blocker.

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

---

## Deliberately deferred (per Phase B §17)

- **HTTPS binding** — Stage C+ proves IIS/hosting-bundle/SQL Server mechanics over plain HTTP on port 8080. Certificate provisioning and HTTPS binding are Phase E deployment-checklist items (CLAUDE.md §23), not part of this rehearsal.
- **Migration-login tightening** — `atmos_app` currently has both runtime and schema-change rights; splitting these is a small Phase E hardening step, not a Phase C blocker.

---

## File transfer note

Deploying to the VM required moving a ~32MB published build across — too large for WinRM's inline command channel. Used a temporary local HTTP server (`python3 -m http.server`, bound to the `virbr0` bridge IP `192.168.122.1`) for the VM to pull from via `Invoke-WebRequest`, which needed one local firewall rule (`ufw allow from 192.168.122.0/24 to any port 8899 proto tcp`) opened for the lab subnet only. The temporary server was stopped after use; the firewall rule was left in place since it will be needed again for the next deploy in Phase D/E — narrow scope (lab-only subnet, single port), low risk, but worth knowing it's there.
