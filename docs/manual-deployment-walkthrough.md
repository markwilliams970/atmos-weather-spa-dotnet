# Manual Deployment Walkthrough — Atmos Weather on IIS + SQL Server

**Purpose:** a hands-on, didactic walkthrough for deploying Atmos Weather to a **fresh** Windows Server 2025 VM by hand — publishing the app yourself, standing up the database yourself, wiring up IIS yourself — so the mechanics are yours, not just something a script did for you. This is written for someone who knows .NET conceptually but hasn't touched IIS/ASP.NET Core hosting in a while.

This project already has a fully automated version of this exact deployment (used to stand up the `win2025app` lab VM via WinRM). Every major section below ends with a **📎 How this project automates it** box showing the real command(s) used, and where to find the rest. Use those to check your work, not to skip the manual steps.

**What you'll need:**
- A fresh Windows Server 2025 VM (or similar) with administrator access — RDP or console access, not just WinRM.
- This repository, either cloned onto the VM or available on a machine you can publish from and copy artifacts across.
- About an hour, if you read the "why" as you go rather than just pasting commands.

---

## Part 0 — What "ASP.NET Core on IIS" actually means now

If the last time you touched IIS was classic ASP.NET (`System.Web`), the model has changed in a way worth understanding before you start clicking around, or the pieces won't make sense.

**Classic ASP.NET:** IIS's worker process (`w3wp.exe`) loads the .NET CLR directly and runs your application *inside* IIS. IIS and your app are the same process.

**ASP.NET Core (what this app is):** your app is a **self-contained console application** with its own embedded web server, Kestrel, built in. It can run completely standalone — `dotnet Atmos.Web.dll` starts a real, working web server with no IIS involved at all (you've been doing exactly this all through local development). IIS's job, when you put it in front of an ASP.NET Core app, is different: it acts as a **reverse proxy** — a component called the **ASP.NET Core Module (ANCM)** sits inside IIS, and its only job is to start your `dotnet Atmos.Web.dll` process, forward HTTP requests to it, and restart it if it crashes. In this project's configuration ("in-process hosting," the default and what you'll set up here), ANCM actually hosts your app's request pipeline inside the IIS worker process for performance, but conceptually it's still "IIS launches and manages your app's own process."

This matters practically in two ways you'll hit below:
1. The IIS **application pool** you create for this app must have **"No Managed Code"** as its .NET CLR version — there is no .NET Framework CLR for IIS to load, because your app brings its own runtime.
2. Almost everything you'd configure "in IIS" for a classic ASP.NET app (environment variables, connection strings) — this app reads from its own configuration system (`appsettings.json` + environment variables), not from IIS/`machine.config`. The one thing IIS's `web.config` *does* still control for an ASP.NET Core app is exactly the environment variables passed to the process ANCM launches — which is why you'll be editing `web.config`'s `<environmentVariables>` later, not because it's "IIS config" in the classic sense, but because it's the mechanism for passing config *into* your app's process.

---

## Part 1 — Prerequisites on the VM

### 1.1 Enable IIS

**GUI:** Server Manager → *Add Roles and Features* → Server Roles → check **Web Server (IIS)**. Accept the default role services; you don't need anything beyond the basics (Common HTTP Features, Health and Diagnostics, Security, Application Development's Static Content, plus the defaults) — this app doesn't use classic ASP.NET, ASP, or CGI features.

**PowerShell equivalent:**
```powershell
Install-WindowsFeature -Name Web-Server -IncludeManagementTools
```

**Verify:** open **Internet Information Services (IIS) Manager** from the Start menu. You should see the server node with a **Default Web Site** already running on port 80 — leave it alone; you'll create a *new* site for this app rather than replacing it.

> 📎 **How this project automates it:** the lab VM (`win2025app`) already had IIS installed when this project first inspected it — `Install-WindowsFeature` was never actually exercised by this project's own automation. Treat the command above as standard guidance, not something battle-tested here.

### 1.2 Install the .NET 10 Hosting Bundle

This is the piece that actually gives IIS the ASP.NET Core Module (ANCM) described in Part 0, plus the .NET runtime itself. Without it, IIS has no idea what to do with an ASP.NET Core app.

**Download:** get the **Hosting Bundle** (not just the SDK, not just the runtime — specifically the *Hosting Bundle*, which is packaged for exactly this purpose) from Microsoft's .NET download page for .NET 10, Windows, x64.

**Install:** run the downloaded `dotnet-hosting-10.x.x-win.exe` installer. Click through it (or run it silently: `dotnet-hosting-10.x.x-win.exe /install /quiet /norestart`).

**Restart IIS** so it picks up the newly-registered module:
```powershell
iisreset /noforce
```

**Verify:**
```powershell
dotnet --info
```
should now list an `.NETCoreApp` runtime. And in IIS Manager, click the server node → **Modules** (double-click it) → you should see `AspNetCoreModuleV2` in the list.

> 📎 **How this project automates it:**
> ```powershell
> $url = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.0/dotnet-hosting-10.0.0-win.exe"
> Invoke-WebRequest -Uri $url -OutFile "C:\Windows\Temp\dotnet-hosting-10.exe" -UseBasicParsing
> Start-Process -FilePath "C:\Windows\Temp\dotnet-hosting-10.exe" `
>   -ArgumentList "/install /quiet /norestart /log C:\Windows\Temp\hosting-install.log" -Wait
> iisreset /noforce
> ```
> This is exactly the command that installed the Hosting Bundle on `win2025app` during this project's Phase C. See `docs/phase-c-build-environment.md`.

### 1.3 Install (or verify) SQL Server

If your fresh VM has no SQL Server at all, install **SQL Server 2022 Developer Edition** (free, full-featured, license terms restrict it to non-production/dev use — appropriate for a learning VM). Run the installer, choose **Basic** or **Custom** install:

- **Instance:** default instance is fine (`MSSQLSERVER`) unless you have a reason to name one.
- **Authentication mode:** choose **Mixed Mode** (SQL Server and Windows Authentication) during setup, and set an `sa` password when prompted. This app connects via a dedicated **SQL login** (not Windows integrated auth), so Mixed Mode is required — Windows-only auth mode won't let you create that login at all.
- Install **SQL Server Management Studio (SSMS)** too if it's not already on the box — you'll want it for Part 3.

**Verify:** open SSMS, connect to `localhost` (Windows Authentication is fine for *this* connection — you're connecting as the admin you're logged in as), and confirm you can see the instance and expand **Databases**. If you're verifying an *existing* install rather than one you just set up yourself (the common case on this lab VM), also confirm Mixed Mode explicitly rather than assuming it — either **Object Explorer → right-click the server → Properties → Security** and check "SQL Server and Windows Authentication mode" is selected, or from a query window:
```sql
SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly')
  WHEN 1 THEN 'Windows-only (wrong for this app)'
  ELSE 'Mixed (correct)'
END AS AuthMode;
```
Part 3.2's `atmos_app` SQL login simply won't be able to authenticate at all if this comes back Windows-only — worth catching here rather than as a confusing login failure three parts later.

> 📎 **How this project automates it:** SQL Server 2022 Developer Edition (Mixed Mode) was already installed on `win2025app` before this project touched it — never installed by this project's own automation either. If you're setting up a genuinely blank VM, the manual steps above are your primary guidance; there's no scripted equivalent in this repo to fall back on for the engine install itself. `scripts/lab/vm-create.ps1` does run the exact reachability and Mixed Mode check shown above as a prerequisite before touching anything, and fails immediately with a clear message if either isn't true, rather than proceeding and failing confusingly later.

---

## Part 2 — Publish the app

### 2.1 Decide where to build

You have two reasonable options:

- **Build on your dev machine, copy the output.** The VM only needs the Hosting Bundle (runtime), not the full SDK. This is what this project's own automation does, and it's the more realistic "real deployment" experience — production servers usually don't have a full SDK sitting around.
- **Clone the repo and build directly on the VM.** Simpler for a learning exercise, but means installing the full .NET 10 SDK on the VM (not just the Hosting Bundle) — `winget install Microsoft.DotNet.SDK.10` or the SDK installer from the same download page.

Either is fine. The rest of this guide assumes you end up with a **published output folder** on the VM one way or another.

### 2.2 Publish

From the repo root (on whichever machine you're building on):
```bash
dotnet publish src/Atmos.Web/Atmos.Web.csproj -c Release -o ./publish
```

Take a look at what's actually in `./publish` before moving on — this is worth understanding, not just trusting:

| File/folder | What it is |
|---|---|
| `Atmos.Web.dll` (+ `Atmos.Core.dll`) | Your compiled app — this is what `dotnet` actually runs. |
| `Atmos.Web.exe` | A native apphost — lets you run `.\Atmos.Web.exe` directly without typing `dotnet Atmos.Web.dll`. IIS doesn't use this; ANCM invokes `dotnet Atmos.Web.dll` per `web.config`. |
| `web.config` | **Auto-generated by `dotnet publish`.** Tells IIS/ANCM how to launch your app. Don't hand-edit the parts publish generates — you *will* edit the environment-variables section later, but that's additive, not a rewrite. |
| `appsettings.json`, `appsettings.Production.json` | Your app's own configuration — see Part 4.5 for why `appsettings.Production.json` is safe to ship as-is (it has no secrets in it) and why the connection string is *not* in here. |
| `wwwroot/` | Static files (CSS/JS) served directly by IIS's static-file handling, bypassing your app entirely for performance. |
| A pile of `.dll` files | Your NuGet dependencies (EF Core, Serilog, etc.) — framework-dependent deployment means these are your app-specific dependencies; the shared .NET runtime itself comes from the Hosting Bundle you installed in Part 1.2, not from here. |

This is a **framework-dependent** deployment (the default, and what `-c Release` alone gives you) — it relies on the Hosting Bundle's shared runtime rather than bundling the entire .NET runtime into the output. That's why Part 1.2 had to happen first.

### 2.3 Get the bundle onto the VM

If you built on the VM directly, you're already there. If you built elsewhere, copy the `publish` folder over — RDP clipboard/drag-and-drop, a shared folder, or `scp`/similar if you have SSH set up. (This project's own automation zips the folder and serves it from a temporary local HTTP server for the VM to pull via `Invoke-WebRequest` — overkill for a one-person manual walkthrough with RDP access; just copy the files.)

Put it somewhere sensible, e.g. `C:\inetpub\AtmosWeb` — you'll formalize this as the IIS site's physical path in Part 4.

> 📎 **How this project automates it:** `dotnet publish src/Atmos.Web/Atmos.Web.csproj -c Release -o /tmp/atmos-publish`, then a temporary `python3 -m http.server` bridges the file across from the Linux dev machine to the VM, since there's no shared filesystem between them (see `docs/phase-c-build-environment.md`'s "File transfer note"). On your own VM with RDP access, you don't need any of that — a straightforward copy is the *more* realistic manual-deployment experience.

---

## Part 3 — Database setup

This whole part is the manual, one-statement-at-a-time version of what `scripts/lab/vm-create.ps1`'s "SQL Server persistence layer" section (see that script's own comments) does programmatically and idempotently. Follow 3.1–3.4 as written for a **fresh** VM that has never had `AtmosDb`/`atmos_app` on it; if you're redeploying to a VM that already does, see the **"Redeploying"** callout under each step instead of the base instructions — trying to `CREATE` something that already exists is a SQL error, not a no-op, unlike the automated script.

### 3.1 Connect and create the database

Open SSMS, connect to your SQL Server instance (Windows Authentication, as the admin), and create the database:

```sql
CREATE DATABASE AtmosDb;
```

(You can do this via SSMS's Object Explorer GUI too — right-click **Databases** → **New Database** — but the one-line SQL is simpler and is exactly what happens either way.)

**Redeploying?** If `AtmosDb` already exists, this errors with `Database 'AtmosDb' already exists.` — that's expected, not a problem. Skip straight to 3.2; nothing here needs to be re-run.

### 3.2 Create a dedicated, least-privilege login

**Don't use `sa` for the application.** Create a login scoped to exactly what this app needs — this is a real security practice, not a formality: if this login's credentials ever leaked, the blast radius is "read/write one table," not "everything on this SQL Server instance."

```sql
-- On the server (master database context):
CREATE LOGIN atmos_app WITH PASSWORD = 'ChooseYourOwnStrongPassword!1', CHECK_POLICY = ON;
```

Switch your SSMS query window to the `AtmosDb` database (the dropdown at the top of the query toolbar, or `USE AtmosDb;`), then:

```sql
CREATE USER atmos_app FOR LOGIN atmos_app;
ALTER ROLE db_datareader ADD MEMBER atmos_app;
ALTER ROLE db_datawriter ADD MEMBER atmos_app;
GRANT CREATE TABLE TO atmos_app;
GRANT ALTER ON SCHEMA::dbo TO atmos_app;
```

The `CREATE TABLE`/`ALTER ON SCHEMA` grants are there because this same login will apply the EF Core migration in the next step (which creates the table) — in a more locked-down production setup you'd use a *separate*, more-privileged login just for running migrations and drop those two grants from the app's runtime login afterward. For a learning VM, one login covering both is a reasonable simplification.

**Write the password down somewhere you'll have it in Part 4.5** — you'll need it for the connection string.

**Redeploying?** `CREATE LOGIN atmos_app` errors with `The server principal 'atmos_app' already exists.` if it's already there — reset its password instead (this is exactly what `vm-create.ps1` does on every run, so the login's password always matches whatever it's about to write into `web.config`):
```sql
ALTER LOGIN atmos_app WITH PASSWORD = 'YourNewPassword!1';
```
`CREATE USER atmos_app FOR LOGIN atmos_app` will similarly error with `User, group, or role 'atmos_app' already exists` if the database user already exists — skip that one line. The four statements after it (`ALTER ROLE ... ADD MEMBER` twice, both `GRANT`s) are all safe to re-run unconditionally: SQL Server treats adding an already-present role member, or re-granting an already-granted permission, as a harmless no-op rather than an error, so just run them again to be sure they're in place.

### 3.3 Apply the EF Core migration

Per this project's own rule (CLAUDE.md §22): **no runtime `CREATE TABLE IF NOT EXISTS`-style schema magic.** Schema changes are explicit, versioned EF Core migrations, applied as a deliberate deployment step. There are two ways to actually run one:

**Option A — you have the .NET SDK somewhere (dev machine or the VM):**
```bash
dotnet ef migrations script --idempotent --project src/Atmos.Web --startup-project src/Atmos.Web -o migrate.sql
```
This produces a plain `.sql` file — no EF tooling needed to *run* it, only to *generate* it. `--idempotent` means it's safe to run against a database that already has some/all of the migration applied (it checks a `__EFMigrationsHistory` table internally), which matters because you'll likely run this more than once while learning.

Copy `migrate.sql` to the VM (same options as Part 2.3), then run it — either open it in SSMS and hit **Execute**, or from a command prompt:
```powershell
sqlcmd -S localhost -E -d AtmosDb -i migrate.sql
```
(`-E` = trusted/Windows-auth connection, which is fine here since you're running this as an admin doing a one-time setup step — this is different from the app's own *runtime* connection, which uses the `atmos_app` SQL login from 3.2.)

**Option B — no SDK anywhere handy:** you'd need to hand-write the `CREATE TABLE` statement matching the current migration. Not recommended — Option A is one command and guarantees the schema matches exactly what the app's EF Core model expects.

**Redeploying?** Nothing special to do — `--idempotent` is exactly for this. Re-running `migrate.sql` against a database that already has the migration applied is a deliberate, supported no-op (it checks `__EFMigrationsHistory` and skips anything already there), so this step is identical whether it's the first time or the fifth.

### 3.4 Verify

Back in SSMS, expand `AtmosDb` → **Tables** — you should see `RecentSearch` and `__EFMigrationsHistory`. Expand `RecentSearch` → **Columns** and sanity-check they look like `Id, SessionId, Label, Latitude, Longitude, ElevationMeters, Units, LocationType, CreatedUtc, LastAccessedUtc`.

Then confirm the login/user/grants from 3.2 actually took — easy to get right for the database but wrong for the security setup, and a broken grant fails silently until the app actually tries to write:
```sql
-- Login exists at the server level:
SELECT name, type_desc, create_date FROM sys.server_principals WHERE name = 'atmos_app';

-- User exists in AtmosDb and is mapped to that login (run against AtmosDb, not master):
SELECT name, type_desc FROM sys.database_principals WHERE name = 'atmos_app';

-- Role memberships:
SELECT r.name AS RoleName
FROM sys.database_role_members drm
JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
JOIN sys.database_principals m ON drm.member_principal_id = m.principal_id
WHERE m.name = 'atmos_app';
-- expect two rows: db_datareader, db_datawriter
```
If the last query comes back empty, the `ALTER ROLE ... ADD MEMBER` statements from 3.2 didn't run (or ran against the wrong database context — double-check you switched to `AtmosDb` first, not `master`) — the login will authenticate fine but every query the app makes will fail with a permission error, which is a more confusing thing to debug from the app side than from here.

> 📎 **How this project automates it:** `scripts/lab/vm-create.ps1`'s "SQL Server persistence layer" section runs the idempotent form of everything in 3.1–3.2 (the same `IF NOT EXISTS` / `CREATE-or-ALTER LOGIN...WITH PASSWORD` pattern shown in the "Redeploying?" callouts above, every time, not just on a second run) as one script via `sqlcmd -S localhost -E -i`, then applies `migrate.sql` from 3.3 as its own step. `scripts/lab/deploy-to-vm.sh` generates that `migrate.sql` with the identical `dotnet ef migrations script --idempotent` command and transfers it to the VM via the same temporary HTTP bridge mentioned in Part 2.3. The verification queries in 3.4 above are the same ones `vm-create.ps1` prints as part of its own pass/fail report at the end of a deploy.

---

## Part 4 — IIS configuration

### 4.1 Copy the published files into place

If you haven't already, make sure your published output (Part 2) lives at a real, permanent path — this guide uses `C:\inetpub\AtmosWeb`, but any path works as long as you're consistent below.

### 4.2 Create the application pool

**GUI:** IIS Manager → **Application Pools** (left tree) → **Add Application Pool...**
- **Name:** `AtmosWebPool`
- **.NET CLR version:** **No Managed Code** — this is the detail from Part 0 that trips people up coming from classic ASP.NET. There is no CLR for IIS to load; your app brings its own.
- **Managed pipeline mode:** Integrated (default, doesn't really matter for "No Managed Code" but leave it default).

Leave the identity as the default (`ApplicationPoolIdentity`) — this creates a dedicated, low-privilege virtual account (`IIS AppPool\AtmosWebPool`) that you'll grant specific permissions to below, rather than running as a more powerful built-in account.

**PowerShell equivalent:**
```powershell
Import-Module WebAdministration
New-WebAppPool -Name "AtmosWebPool"
Set-ItemProperty "IIS:\AppPools\AtmosWebPool" -Name "managedRuntimeVersion" -Value ""
```

### 4.3 Create the site

**GUI:** IIS Manager → **Sites** → **Add Website...**
- **Site name:** `AtmosWeb`
- **Application pool:** `AtmosWebPool` (click Select... and pick the one you just made)
- **Physical path:** `C:\inetpub\AtmosWeb`
- **Binding:** Type `http`, leave IP address as `All Unassigned`, pick a port that isn't already taken — **not 80**, since `Default Web Site` already owns that. This guide uses **8080**.

**PowerShell equivalent:**
```powershell
New-Website -Name "AtmosWeb" -PhysicalPath "C:\inetpub\AtmosWeb" -ApplicationPool "AtmosWebPool" -Port 8080
```

### 4.4 Filesystem permissions

The app pool identity needs to actually be able to *read* the files you just deployed, and *write* to the stdout log folder ANCM uses for startup diagnostics.

**GUI:** right-click `C:\inetpub\AtmosWeb` in File Explorer → **Properties** → **Security** tab → **Edit...** → **Add...** → type `IIS AppPool\AtmosWebPool` → **Check Names** (it should resolve — this confirms the app pool actually exists) → OK. Grant **Read & execute**. Then do the same for the `logs` subfolder specifically, granting **Modify** (write access) there.

**PowerShell equivalent:**
```powershell
icacls "C:\inetpub\AtmosWeb" /grant "IIS AppPool\AtmosWebPool:(OI)(CI)RX" /T
icacls "C:\inetpub\AtmosWeb\logs" /grant "IIS AppPool\AtmosWebPool:(OI)(CI)M" /T
```

### 4.5 Wire up secrets and environment via `web.config`

This is the part Part 0 flagged as different from classic ASP.NET. Your app needs two pieces of environment-specific configuration to run correctly:

1. `ASPNETCORE_ENVIRONMENT=Production` — tells the app to load `appsettings.Production.json` (already in your published output, already committed to the repo, and deliberately **contains no secrets** — just logging configuration that differs by environment; see `docs/logging.md`).
2. `ConnectionStrings__AtmosDb` — the actual SQL connection string, **including the `atmos_app` password from Part 3.2**. This is a real secret and is deliberately **not** in any `appsettings.*.json` file, committed or not — it only ever lives here, in this one server-side `web.config`, or wherever your own deployment process chooses to inject it. (See `docs/logging.md`'s deployment note for the full reasoning — this project actually got this wrong once, committed a connection string into a file that later got overwritten by a redeploy, and fixed it by moving to exactly this pattern.)

**Open `C:\inetpub\AtmosWeb\web.config`** in Notepad (or your editor of choice) — it's plain XML. You'll see something like:
```xml
<aspNetCore processPath="dotnet" arguments=".\Atmos.Web.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
```

Add an `<environmentVariables>` child element inside `<aspNetCore>`:
```xml
<aspNetCore processPath="dotnet" arguments=".\Atmos.Web.dll" stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="ConnectionStrings__AtmosDb" value="Server=localhost;Database=AtmosDb;User Id=atmos_app;Password=YOUR_PASSWORD_HERE;TrustServerCertificate=True" />
  </environmentVariables>
</aspNetCore>
```

(Also flip `stdoutLogEnabled` to `true` for now, as shown above — you want startup errors visible while you're getting this running for the first time. You can turn it back off once everything's stable, per Part 5.)

Why the double-underscore in `ConnectionStrings__AtmosDb`? ASP.NET Core's configuration system treats environment variables as a flat list, and uses `__` (double underscore) to represent the `:` nesting you'd see in `appsettings.json`'s `{ "ConnectionStrings": { "AtmosDb": "..." } }`. This is a general ASP.NET Core convention, not specific to this app.

**Save the file.** No restart command needed yet — you'll do that as part of first-run in Part 5, and any time you change `web.config` afterward, IIS picks it up automatically (changing `web.config` recycles the app pool on its own).

> 📎 **How this project automates it:** this exact XML structure is produced by a small PowerShell script that loads `web.config` as XML, appends the two `<environmentVariable>` elements, and saves it back — see `docs/logging.md`'s "Deployment note" section for the literal script. **Important, learned the hard way:** `dotnet publish` regenerates `web.config` from scratch every time. If you redeploy this app later (a new `dotnet publish` + copying files over), you'll need to redo this step — the environment variables don't survive a fresh publish on their own.

### 4.6 Create the log directory

The app writes structured JSON logs to `C:\ProgramData\atmos\logs` in Production (see `docs/logging.md` for what's actually in them and why) — a location distinct from the site's own `logs\stdout` folder, which only captures ANCM's process-startup diagnostics.

```powershell
New-Item -ItemType Directory -Force -Path "C:\ProgramData\atmos\logs"
icacls "C:\ProgramData\atmos" /grant "IIS AppPool\AtmosWebPool:(OI)(CI)M" /T
```

There's no GUI-only way to do the `icacls` grant that's meaningfully simpler than the command above — File Explorer's Security tab works identically to Part 4.4 if you'd rather click through it.

### 4.7 Firewall

If you want to reach this site from outside the VM (rather than just testing from inside it via RDP), open the port you bound in 4.3:

```powershell
New-NetFirewallRule -DisplayName "AtmosWeb HTTP 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

---

## Part 5 — First run and verification

Start the site (if it isn't already — `New-Website`/the GUI wizard usually starts it automatically):

**GUI:** IIS Manager → Sites → `AtmosWeb` → right panel → **Start**, if it shows as stopped.

**PowerShell:**
```powershell
Start-WebAppPool -Name "AtmosWebPool"
Start-Website -Name "AtmosWeb"
```

**Check the health endpoint first** — it's the single fastest way to know "is the app even running and can it reach the database":
```powershell
Invoke-WebRequest -Uri "http://localhost:8080/healthz" -UseBasicParsing
```
You want `Healthy` back. If you get a `503`, the app process failed to start — go to Part 6.

**Then exercise the real app:** browse to `http://localhost:8080/` (from the VM itself, or from another machine on the network if you opened the firewall). Search a ZIP code. You should get a real forecast.

**Confirm the database round-trip actually happened** — back in SSMS:
```sql
SELECT TOP 5 * FROM AtmosDb.dbo.RecentSearch ORDER BY LastAccessedUtc DESC;
```
You should see the search you just did.

**Confirm structured logging is working:**
```powershell
Get-Content (Get-ChildItem "C:\ProgramData\atmos\logs\atmos-*.log" | Select-Object -Last 1).FullName -Tail 5
```
Each line is one JSON object. Look for `"EnvironmentName":"Production"` and notice that several consecutive lines share the same `"TraceId"` value — that's the request-correlation behavior `docs/logging.md` describes, and it's a nice thing to have actually seen work with your own eyes rather than taken on faith.

Once you've confirmed everything works, you can turn `stdoutLogEnabled` back to `false` in `web.config` (Part 4.5) — the `C:\ProgramData\atmos\logs` JSON logs are the real, ongoing observability story; stdout capture is only useful for diagnosing startup failures.

---

## Part 6 — Troubleshooting common failures

ASP.NET Core-on-IIS errors are notoriously terse. Here's what the common ones actually mean for this app specifically:

| Symptom | Likely cause | Where to look |
|---|---|---|
| **HTTP 503** immediately, on every request | The app process failed to start entirely — ANCM couldn't launch it, or it crashed on startup. | `C:\inetpub\AtmosWeb\logs\stdout_*.log` (only populated if `stdoutLogEnabled="true"`, Part 4.5) — this is almost always where the real exception is. |
| **HTTP 500.30 - ASP.NET Core app failed to start** | Same as above, IIS's own error page for it. | Same as above. |
| Stdout log shows `The ConnectionString property has not been initialized` | `ConnectionStrings__AtmosDb` environment variable is missing or misspelled in `web.config` — the app's own config binding never found a value. | Re-check Part 4.5's XML, especially the double underscore. |
| Stdout log shows a SQL login/authentication failure | Wrong password in the connection string, or Mixed Mode auth wasn't actually enabled on the SQL Server instance (Part 1.3). | Verify in SSMS: Server Properties → Security → confirm "SQL Server and Windows Authentication mode" is selected; restart the SQL Server service if you just changed it. |
| Stdout log shows a permission-denied writing to `C:\ProgramData\atmos\logs` | The `icacls` grant in 4.6 didn't take, or you granted it to the wrong app pool name. | Re-run the `icacls` command; verify the app pool name matches exactly (`IIS AppPool\AtmosWebPool`, case as shown). |
| **HTTP 502.5 - Process Failure** | ANCM couldn't even launch `dotnet.exe` — usually means the Hosting Bundle isn't actually installed, or `web.config`'s `processPath`/`arguments` got corrupted by a manual edit. | Re-run `dotnet --info` on the VM directly; re-verify Part 1.2. |
| Site loads, but static CSS/JS is missing (unstyled page) | Physical path in the site binding doesn't actually point at your published `wwwroot` folder, or the deploy copied files into the wrong subfolder. | Confirm `C:\inetpub\AtmosWeb\wwwroot\css\site.css` actually exists. |
| Everything works from the VM itself but not from another machine | Firewall (Part 4.7) not opened, or binding is restricted to an IP that doesn't match how you're connecting. | Re-check the firewall rule; try `Test-NetConnection -ComputerName <vm-ip> -Port 8080` from the other machine. |

**General diagnostic habit worth building:** whenever something's broken and you're not sure why, `stdoutLogEnabled="true"` + a fresh request + reading the newest `stdout_*.log` file answers "did the app even start" faster than almost anything else. Once the app *is* running, `C:\ProgramData\atmos\logs`'s structured JSON logs are far more useful for "why did this specific request behave oddly."

---

## Part 7 — Deliberately out of scope here

- **HTTPS.** This walkthrough (and this project's own automated VM) serves plain HTTP. A real deployment needs a certificate (self-signed for a lab, a real one otherwise) bound in IIS and `app.UseHsts()`'s effects taken seriously — CLAUDE.md defers this to a later "Phase E" deployment-hardening pass that hasn't happened yet on the automated VM either, so there's nothing to point you at here.
- **A repeatable CI/CD pipeline.** Everything in this guide, and in this project's own automation, is a one-shot, manually-triggered deployment. Neither is "redeploy on every commit."

---

## Appendix — where the automation actually lives

If you want to compare notes after doing this by hand, or set up a second VM faster next time:

- `docs/phase-c-build-environment.md` — the original Phase C walkthrough of exactly this deployment (IIS + Hosting Bundle install, SQL Server login/schema, IIS site/app-pool creation), run via WinRM against `win2025app` from a Linux dev machine.
- `docs/logging.md` — the logging design, the `C:\ProgramData\atmos\logs` convention, and the `web.config` environment-variable pattern for the connection string (including the deployment mistake that led to using it, which you got to skip by reading this guide first).
- CLAUDE.md §20-22 — the project's standing rules for build/deployment/SQL Server that both this guide and the automation follow.
- `scripts/lab/vm-create.ps1` (+ `scripts/lab/deploy-to-vm.sh` to run it remotely) — this whole guide, formalized: idempotent IIS/Hosting Bundle prerequisite checks, the SQL Server persistence layer (`AtmosDb` + the dedicated `atmos_app` login/user/grants + the current EF Core migration), the published files, `web.config`'s environment variables, the log directory, and the firewall rule — ending in a real end-to-end `/healthz` request through IIS → Kestrel → SQL Server, not just "the commands didn't error." Explicitly does **not** install the SQL Server engine itself (Part 1.3) — verifies it's reachable and in Mixed Mode and fails clearly if not, the same scope boundary this project's own Phase C automation always had. `deploy-to-vm.sh` publishes the app, generates the migration script, and bridges both to the VM the same way Part 2.3's automation note describes. Defaults to a dry run; `--force`/`-Force` actually deploys.
- `scripts/lab/vm-teardown.ps1` (+ `scripts/lab/run-vm-teardown.sh`) — the reverse: stops and removes the `AtmosWeb` site/app pool, deletes the published files, drops the `AtmosDb` database and `atmos_app` login (with before/after query-based validation), removes the firewall rule and `C:\ProgramData\atmos\logs`. Also defaults to a dry run.
- `scripts/lab/_winrm-common.sh` — the shared WinRM connection/file-transfer helpers both VM scripts' wrappers use (NTLM + the OpenSSL legacy-provider workaround, chunked script upload around WinRS's ~8KB command-line ceiling).
- `scripts/lab/dev-harness-create.sh` / `scripts/lab/dev-harness-teardown.sh` — the equivalent create/delete pair for the local Docker dev harness (§ "Local dev SQL Server" in `docs/phase-c-build-environment.md`): the `atmos-sql-dev` container/volume, the applied EF Core migration, and the matching User Secrets entry.

**All four were validated for real, not just dry-run:** `deploy-to-vm.sh --force` as an idempotent redeploy over the already-running site (every check in its report passed, including a live `/healthz` round-trip), then `run-vm-teardown.sh --force` to actually tear that deployment back down again (also fully verified afterward — see `docs/phase-c-build-environment.md`'s "Current state" section); the dev-harness pair the same way. **As of this writing, the VM deployment and the local dev harness are both torn down** — `win2025app` still has IIS/SQL Server installed, just none of the app-specific pieces. Running `deploy-to-vm.sh --force` / `dev-harness-create.sh` again brings each back.
