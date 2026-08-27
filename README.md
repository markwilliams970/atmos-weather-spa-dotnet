# Atmos Weather

A small weather application, being modernized from a TypeScript/Node.js/SQLite single-page app into a layered ASP.NET Core 10 / SQL Server / IIS application — deliberately built and deployed as a **hands-on learning project** for an engineer whose day-to-day is Linux and cloud-native infrastructure, who wants a practical, working refresher on the modern Windows Server / IIS / .NET web stack.

This README is written for that audience: it assumes you're comfortable with software engineering, HTTP, SQL, and Linux/cloud tooling in general, but doesn't assume recent hands-on time with .NET, IIS, or Windows Server specifically.

---

## Contents

- [A. Purpose and Need](#a-purpose-and-need)
- [B. Architecture](#b-architecture)
- [C. Prerequisites and Frameworks](#c-prerequisites-and-frameworks)
  - [What's actually changed since "classic" .NET / ASP.NET](#whats-actually-changed-since-classic-net--aspnet)
- [D. Deployment and Lab Overview](#d-deployment-and-lab-overview)
- [Local Development Quick Start](#local-development-quick-start)
- [Testing](#testing)
- [Configuration](#configuration)
- [Project Structure](#project-structure)
- [Documentation Index](#documentation-index)

---

## A. Purpose and Need

**Atmos Weather** is a small, no-account weather app: search a US ZIP code or city, or drop a pin on a map, and get current conditions, a 24-hour and 7-day forecast, air quality, and an animated weather radar. It stores your last 10 searches in a browser session (no login), remembers your unit preference (imperial/metric) per location, and renders a distinct animated visual theme per weather condition and time of day.

The **original implementation** (`../atmos-weather-spa`, a sibling repository) is a deliberately minimal single-file TypeScript/Node.js app: one `weather-server.ts` containing SQLite persistence, session handling, HTML/CSS generation, and browser JavaScript all in one file, with no framework, no bundler, and no automated tests. It's a good example of "get it working with the fewest moving parts" — and this repository treats it as a **read-only behavioral reference**, never modified, only observed and ported from.

**Why modernize it at all?** The original app isn't broken. The point of this project isn't to fix Atmos Weather — it's to use a small, well-understood application as the vehicle for **actually building and deploying** a real ASP.NET Core application against the stack a great many enterprises still run in production: IIS, Windows Server, SQL Server. That combination is easy to read *about* and much less common to have recent hands-on time with if your daily work is Kubernetes, Linux containers, and managed cloud databases. This project exists to close that gap with a real, working, tested, deployed application — not a tutorial-sized "Hello World."

Concretely, "modernize" means:

- A genuinely layered architecture (domain models, application services, persistence, presentation) instead of one file.
- Real SQL Server persistence via EF Core migrations, not a SQLite file created ad hoc at runtime.
- Automated tests at three levels (unit, integration, browser) — the original has none.
- Structured, correlated logging designed to make future APM instrumentation cheap — the original logs to stdout only.
- A real IIS + SQL Server deployment (and now, a fully scripted, idempotent, tested create/delete pipeline for that deployment) — not just `npm start`.

**Explicit non-goals:** this is not a production weather service (no SLA, no scale requirements, no multi-tenancy), doesn't require user accounts, and deliberately defers HTTPS/CI hardening to a later phase (see [`CLAUDE.md`](./CLAUDE.md) §23, §27 for what's preserved/improved/deferred and why).

---

## B. Architecture

Atmos Weather is a **hybrid server-rendered application**, not a single-page app:

- **Razor Pages** render the HTML shell (search bar, layout, tab structure) — four pages: `/` (search), `/weather` (forecast), `/map` (map-picker deep link), `/about`.
- **ASP.NET Core Minimal API** serves a small JSON surface (`/api/weather`, `/api/geocode`, `/api/recent`, `/api/air-quality`, `/api/elevation`, `/api/nearby-place`, `/api/radar/frame`) consumed entirely by browser `fetch()` calls.
- **Plain browser JavaScript** (ES modules, no bundler, no framework) owns everything client-local: the interactive map picker, SVG charts/gauges, radar tile rendering, animated sky themes, tab switching, and autocomplete. The browser never talks to an external weather API directly — every external call is proxied and shaped by the server.

```
┌─────────────┐        HTTP          ┌────────────────────────────────────┐
│   Browser   │ ───────────────────▶ │         ASP.NET Core (Kestrel)      │
│ (ES modules,│ ◀─────────────────── │            Atmos.Web                │
│  no SPA fw) │                      │                                      │
└─────────────┘                      │   Pages/          Endpoints/         │
                                      │   (Razor, HTML)   (Minimal API,      │
                                      │                     JSON)            │
                                      └─────────┬───────────────┬────────────┘
                                                │               │
                                                ▼               ▼
                                      ┌──────────────────┐  ┌───────────────────┐
                                      │  EF Core          │  │  Application       │
                                      │  (Atmos.Web/Data) │  │  Services          │
                                      │                    │  │  (Atmos.Core +     │
                                      └─────────┬──────────┘  │   Atmos.Web/       │
                                                │              │   Services)        │
                                                ▼              └─────────┬──────────┘
                                      ┌──────────────────┐              ▼
                                      │  SQL Server        │   ┌───────────────────┐
                                      │  (RecentSearch      │   │  External APIs     │
                                      │   table only)        │   │  Open-Meteo,       │
                                      └──────────────────┘   │  Zippopotam,        │
                                                              │  RainViewer,        │
                                                              │  Overpass/Nominatim │
                                                              └───────────────────┘
```

**Project layout:**

| Project | What it is |
|---|---|
| `src/Atmos.Core` | Shared, dependency-light library: domain models (`Location`, `WeatherForecast`, …), WMO weather-code mapping, unit conversions, `IGeocodingService`/`IWeatherService` and their `HttpClient`-backed implementations. Shared by both `Atmos.Web` and `Atmos.Cli` — it exists because two executables concretely need the same code, not speculatively. |
| `src/Atmos.Web` | The ASP.NET Core application: `Pages/` (Razor), `Endpoints/` (Minimal API), `Services/` (elevation, nearby-place, air quality, radar, recent-search — all web-only), `Data/` (EF Core `DbContext`, entity, migrations), `Infrastructure/` (session cookie middleware, exception handling, same-origin check), `wwwroot/` (CSS + 9 JS modules). |
| `src/Atmos.Cli` | A small standalone console tool (`atmos <zip>`) reusing `Atmos.Core`'s services — no web server, no database, no session. |

**Design principles that shaped this, in brief** (see [`ARCHITECTURE.md`](./ARCHITECTURE.md) for the full write-up, and [`docs/phase-a-assessment.md`](./docs/phase-a-assessment.md)/[`docs/phase-b-target-architecture.md`](./docs/phase-b-target-architecture.md) for the complete decision-by-decision rationale from when these calls were made):

- **External API responses never leak past a service boundary.** Every third-party JSON shape (Open-Meteo, Zippopotam, RainViewer, …) is mapped into an internal domain model before anything else in the app sees it.
- **No repository layer over EF Core.** EF Core's `DbContext` *is* the persistence abstraction; wrapping it in another interface would be indirection with no payoff for an app with one entity.
- **Session, not accounts.** Recent searches are scoped to a random session cookie, capped at 10, with no login — matching the original app's actual product shape rather than adding auth nobody asked for.
- **External-API resilience is explicit, not implicit.** Core-path calls (geocoding, forecast) get one retry and a timeout; cosmetic enhancement calls (elevation, nearby-place, air quality) fail fast and degrade gracefully — a forecast is never held hostage by a decorative feature.
- **Structured logging designed for future APM, not yet instrumented.** Serilog with JSON output and `Activity`-based trace/span correlation is already in place — see [`docs/logging.md`](./docs/logging.md) — with an explicit, documented boundary around what's *not* done yet (no Datadog package, no exporters, no manual spans).

---

## C. Prerequisites and Frameworks

**To build and run locally, you need:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — the only hard requirement; everything else is optional depending on what you're doing.
- Git.
- Docker — to run the local development SQL Server (see [Local Development Quick Start](#local-development-quick-start)). You can point at any reachable SQL Server instead if you'd rather not use Docker.
- The `dotnet-ef` local tool, restored automatically via `dotnet tool restore` (manifest at `dotnet-tools.json`) — needed to apply/generate EF Core migrations.

**Only if you're doing IIS/Windows Server deployment work** (see [Part D](#d-deployment-and-lab-overview)):

- A Windows Server host with IIS and the [.NET 10 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0) installed.
- SQL Server (any edition; Developer Edition is free and what this project's own lab uses), with Mixed Mode authentication enabled.

**Frameworks and libraries actually in use:**

| Layer | Technology |
|---|---|
| Web framework | ASP.NET Core 10 — Razor Pages (HTML) + Minimal API (JSON) |
| ORM / migrations | Entity Framework Core 10, SQL Server provider |
| Logging | Serilog (`Serilog.AspNetCore`), JSON + console sinks, `Serilog.Enrichers.Span` for trace/span correlation |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly under the hood) — timeouts and retry on the core-path external HTTP calls |
| Unit / integration testing | xUnit, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<T>`), EF Core's SQLite provider as a fast integration-test stand-in for SQL Server |
| Browser testing | Microsoft.Playwright (headless Chromium) |
| Frontend | Plain ES modules, no bundler, no framework — CSS custom properties for theming |
| Deployment hosting | IIS + the ASP.NET Core Module (ANCM), in-process hosting |

### What's actually changed since "classic" .NET / ASP.NET

If the last time you touched .NET was Framework-era ASP.NET (Web Forms, MVC 5, `System.Web`), several things that used to be true no longer are. This is the single most useful orientation for the audience this README is written for — read it before Part D, it'll make the deployment steps make sense instead of feeling like arbitrary XML editing.

**The runtime itself is different, not just the framework.** ".NET Framework" was one shared, Windows-only runtime installed machine-wide and patched via Windows Update — every app on a box ran against whatever version was installed globally, similar to how a system Python or Java once worked. **.NET Core, and everything since .NET 5, is cross-platform and versioned per-application**, much closer to how you already manage a Go toolchain or a Node.js runtime: you can have .NET 8 and .NET 10 apps running side by side on the same machine with zero conflict, and this entire project was developed and tested on Linux without a Windows machine in sight until the actual IIS deployment step.

**The web server is no longer IIS.** This is the mental model shift that matters most. Classic ASP.NET ran *inside* IIS's worker process (`w3wp.exe`) — the .NET Framework CLR was loaded directly into IIS, and IIS *was* the HTTP server. **ASP.NET Core ships its own embedded web server, Kestrel**, and the app is a genuinely self-contained executable: `dotnet Atmos.Web.dll` (or the native `Atmos.Web.exe` apphost) starts a real, complete, standalone web server — no IIS involved at all. This is exactly the shape of a Go binary or a Node process opening its own listen socket, and it's how this app has been run for the entire local-development portion of this project (`dotnet run --project src/Atmos.Web`). IIS becomes **optional** — when you do put it in front of the app (as this project's deployment does), its job changes to that of a **reverse proxy**: the **ASP.NET Core Module (ANCM)**, a small IIS module, starts your `dotnet Atmos.Web.dll` process, forwards HTTP requests to it, and restarts it if it crashes. (This project uses ANCM's "in-process" hosting mode, where for performance reasons your app's request pipeline actually runs inside the IIS worker process again — but conceptually it's still "IIS launches and supervises your app's own process," not "IIS loads your code into itself.") Practically, this is why the IIS application pool for an ASP.NET Core app must be set to **"No Managed Code"** — there's no .NET Framework CLR for IIS to load, because the app brings its own runtime.

**Configuration is no longer XML-and-registry.** Classic ASP.NET read `web.config`/`machine.config` XML sections, sometimes combined with registry values or `appSettings` blocks scattered across environments with no single model for "where does this setting actually come from." ASP.NET Core has one unified, hierarchical configuration system (`IConfiguration`) that layers `appsettings.json`, environment-specific `appsettings.{Environment}.json` files, environment variables, command-line arguments, and (in development) User Secrets — much closer to twelve-factor config than classic .NET ever offered. The one thing `web.config` *does* still control for an ASP.NET Core app under IIS is the environment variables passed to the process ANCM launches — see [`docs/logging.md`](./docs/logging.md)'s deployment note for exactly where this bit this project once (a secret that briefly had nowhere safe to live, until it was moved here).

**Dependency injection is built in, not bolted on.** Classic ASP.NET had no first-party DI container — teams reached for Unity, Ninject, StructureMap, or Autofac and wired it in themselves. ASP.NET Core has had a real DI container (`IServiceCollection`/`IServiceProvider`) as a foundational part of the framework since day one; every service in this app (`IWeatherService`, `AtmosDbContext`, the Minimal API endpoint handlers themselves) is constructor- or parameter-injected without any third-party package.

**The request pipeline is composable middleware, not registration-heavy modules.** `HttpModule`/`HttpHandler` registration in classic ASP.NET lived in XML and ran implicitly. ASP.NET Core's pipeline is an explicit, readable, ordered sequence of delegates you can see directly in code (`app.UseExceptionHandler(...)`, `app.UseRouting()`, `app.UseSerilogRequestLogging()`, `app.UseMiddleware<SessionCookieMiddleware>()`, …) — `src/Atmos.Web/Program.cs` is the whole pipeline, readable top to bottom.

**Project files got simple again.** Classic `.csproj` files were verbose MSBuild XML that explicitly listed every single source file. Modern "SDK-style" `.csproj` files (every project in this repo) are a handful of lines — source files are picked up by convention, and there's real parity with how lightweight a Go `go.mod` or a Node `package.json` feels.

**Tooling is CLI-first and cross-platform.** The `dotnet` CLI (build, run, test, publish, `dotnet ef`, `dotnet tool`) works identically on Linux, macOS, and Windows — no Visual Studio requirement anywhere in this project's own workflow. Every command in this README and in `scripts/lab/` was run from a Linux terminal.

**Minimal APIs are new.** This app's entire JSON surface (`Endpoints/`) uses ASP.NET Core's Minimal API model (`app.MapGet(...)`, `app.MapPut(...)`) — a lightweight alternative to full MVC controllers, introduced in .NET 6, deliberately chosen here because seven small JSON endpoints didn't justify controller-class ceremony.

**Real in-process integration testing exists now.** `WebApplicationFactory<T>` (used throughout `tests/Atmos.Web.Tests`) boots the actual, real ASP.NET Core pipeline — routing, middleware, model binding, session handling — in memory, against a real (if swapped-out) database, without needing a running IIS instance or a live network port. Getting equivalent test coverage for classic ASP.NET generally meant standing up a real IIS instance and driving it with something like Selenium. See [Testing](#testing) for how this project layers that with genuine browser tests (Playwright) on top.

---

## D. Deployment and Lab Overview

This project deliberately practices deployment to the stack it's meant to teach: **Windows Server + IIS + SQL Server**, not a Linux container. There are two environments:

1. **Local development workstation** (this machine, Linux) — fast iteration, SQL Server running in Docker, `dotnet run` directly.
2. **The lab** — a QEMU/libvirt Windows Server 2025 VM (`win2025app`), reachable over WinRM from this workstation, standing in for a real production IIS/SQL Server host. Everything in this section targets that VM.

**Local development** is covered in the [Quick Start](#local-development-quick-start) below.

**The lab VM deployment is fully scripted** as an idempotent create/delete pair — see `scripts/lab/`:

| Action | Command | What it does |
|---|---|---|
| **Create/redeploy** | `scripts/lab/deploy-to-vm.sh [--force]` | Publishes `Atmos.Web`, generates the current EF Core migration script, transfers both to the VM, and runs `vm-create.ps1` there: idempotent IIS/Hosting-Bundle checks, the SQL Server persistence layer (`AtmosDb` + a dedicated least-privilege `atmos_app` login/user/grants + the migration), the IIS site/app pool, `web.config`'s environment variables (including the connection-string secret — never committed to a file), the structured-log directory, and the firewall rule. Ends with a real end-to-end `GET /healthz` through IIS → Kestrel → SQL Server, not just "the commands didn't error." |
| **Delete** | `scripts/lab/run-vm-teardown.sh [--force]` | The reverse: stops and removes the IIS site/app pool, deletes the published files, **drops** the `AtmosDb` database and the `atmos_app` login (not a truncate), removes the firewall rule and log directory — with an explicit, printed before/after **query-based validation report**, not just an assumption that the commands succeeded. |

Both default to a **dry run** (report current state, change nothing) and require an explicit `--force`/`-Force` to actually act. Both were validated for real against the actual lab VM, including the teardown genuinely dropping and re-verifying the database was gone. `scripts/lab/dev-harness-create.sh`/`-teardown.sh` are the equivalent pair for the local Docker SQL Server used in day-to-day development.

**Explicitly out of scope for this automation:** installing the SQL Server *engine* itself, or IIS on a truly blank machine from zero. The scripts verify these prerequisites are present and fail with a clear message if not, rather than attempting a large, risky silent install this project has never actually exercised.

**For the didactic, do-it-yourself version of all of this** — standing up a fresh VM, publishing by hand, creating the database and login yourself in SSMS, wiring up IIS click by click, understanding *why* each `web.config` edit is needed — see **[`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md)**. It's written for exactly this README's audience (an engineer who wants the actual mechanics, not just a script to run) and every major section cross-references the automated equivalent above so you can compare notes. **[`DEPLOYMENT.md`](./DEPLOYMENT.md)** is the shorter, task-oriented version of this same information — start there if you just need to get something deployed; start with the manual walkthrough if you want to actually learn IIS/SQL Server hosting along the way.

---

## Local Development Quick Start

```bash
git clone <this repo>
cd atmos-weather-spa-dotnet

dotnet tool restore                       # installs dotnet-ef locally

scripts/lab/dev-harness-create.sh         # Docker SQL Server 2022, migrated,
                                           # connection string written to
                                           # .NET User Secrets automatically

dotnet run --project src/Atmos.Web --urls http://localhost:5299
```

Then browse to `http://localhost:5299` and search a ZIP code (try `80002`).

The CLI tool shares the same services:

```bash
dotnet run --project src/Atmos.Cli -- 80002
```

`scripts/lab/dev-harness-create.sh` is idempotent — safe to run again any time; it leaves an already-running container alone. `scripts/lab/dev-harness-teardown.sh [--force]` tears it back down (container, volume, and the User Secrets entry).

---

## Testing

Four test projects, 133 tests total, deliberately layered by speed and fidelity — `dotnet test` (from the repo root) runs everything:

| Project | Count | What it covers |
|---|---|---|
| `tests/Atmos.Core.Tests` | 65 | Pure unit tests — WMO weather-code mapping, unit conversions, Haversine distance, forecast-shaping logic. No ASP.NET Core or database dependency; runs in milliseconds. |
| `tests/Atmos.Cli.Tests` | 1 | A golden-file regression test locking in the CLI's console output, verified byte-for-byte against the original TypeScript CLI. |
| `tests/Atmos.Web.Tests` | 53 | Service-layer tests (external HTTP calls mocked via fixtures) *and* full integration tests via `WebApplicationFactory<Program>` — real routing, session cookies, EF Core against SQLite in-memory as a fast SQL Server stand-in, every external service replaced with a deterministic fake. |
| `tests/Atmos.Web.PlaywrightTests` | 14 | Real headless-Chromium browser tests against a genuinely Kestrel-listening instance of the app: ZIP search, city autocomplete, forecast rendering (all three tabs), recent-search selection, unit switching, the interactive map picker, and radar rendering. |

No test anywhere in this suite depends on a live external API or the internet being reachable — every layer above the pure-unit tests uses deterministic fixtures or fakes, matching this project's own testing philosophy (see [`CLAUDE.md`](./CLAUDE.md) §18).

---

## Configuration

Standard ASP.NET Core layered configuration — `appsettings.json` (defaults, external API base URLs, Serilog pipeline) is overlaid by `appsettings.{Environment}.json`.

**Secrets never live in a committed file, in any environment:**

- **Local development:** the SQL Server connection string is set via [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (`dotnet user-secrets set ...`), which `dev-harness-create.sh` does for you automatically — never written to `appsettings.Development.json`.
- **The IIS deployment:** the connection string lives in `web.config`'s `<aspNetCore><environmentVariables>` as `ConnectionStrings__AtmosDb` (double-underscore is ASP.NET Core's convention for the `:` nesting environment variables can't represent directly), injected by `vm-create.ps1` at deploy time — never in `appsettings.Production.json`, which is safely committed to source control precisely because it contains no secrets.

See [`docs/logging.md`](./docs/logging.md) for the full structured-logging design (what's logged, why those specific boundaries, and how this is deliberately shaped to make future APM instrumentation cheap without actually adding any yet).

---

## Project Structure

```
Atmos.slnx

src/
    Atmos.Core/         Shared domain models, WMO codes, unit conversions,
                         geocoding/weather HTTP-client services
    Atmos.Web/           The ASP.NET Core application
        Pages/            Razor Pages (HTML)
        Endpoints/         Minimal API (JSON)
        Services/          Web-only external services (elevation, AQ, radar, …)
        Data/              EF Core DbContext, entity, migrations
        Infrastructure/    Session middleware, exception handling, DI wiring
        wwwroot/           CSS + 9 vanilla JS modules
    Atmos.Cli/           Standalone console tool, shares Atmos.Core

tests/
    Atmos.Core.Tests/            Pure unit tests
    Atmos.Cli.Tests/             CLI golden-file test
    Atmos.Web.Tests/             Service-layer + WebApplicationFactory integration tests
    Atmos.Web.PlaywrightTests/   Real-browser end-to-end tests

scripts/
    lab/                  Idempotent create/delete tooling for both the local
                           Docker dev harness and the IIS/SQL Server lab VM
                           deployment (see Part D above)

docs/                    Phase-by-phase design/assessment/build docs (index below)
```

---

## Documentation Index

| Document | What's in it |
|---|---|
| [`CLAUDE.md`](./CLAUDE.md) | The governing document for this project: the phased modernization plan, and ~39 sections of standing engineering rules (architecture, security, testing, logging, deployment) this codebase follows. |
| [`ARCHITECTURE.md`](./ARCHITECTURE.md) | Current-state architecture reference — the major design decisions and why, in one place. |
| [`DEPLOYMENT.md`](./DEPLOYMENT.md) | Short, task-oriented deployment reference — the fastest path to "get this deployed," pointing to the deeper docs below for the *why*. |
| [`docs/phase-a-assessment.md`](./docs/phase-a-assessment.md) | The original TypeScript app's full architectural assessment — data flow, external APIs, known defects/technical debt — done before any porting work began. |
| [`docs/phase-b-target-architecture.md`](./docs/phase-b-target-architecture.md) | The detailed target-architecture porting plan: every major decision (project layout, page structure, service boundaries, data model, …) with rationale, and the preserved/improved/removed/deferred disposition of every existing behavior. |
| [`docs/phase-c-build-environment.md`](./docs/phase-c-build-environment.md) | The build/deployment environment's setup history and current state — what's actually installed on the lab VM, and what's currently torn down vs. running. |
| [`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md) | The full didactic, do-it-by-hand IIS + SQL Server deployment guide this README's Part D points to — the primary resource if you want to actually learn the mechanics, not just run a script. |
| [`docs/logging.md`](./docs/logging.md) | The structured-logging design: what's logged and why, the Development/Production file-path switch, and how this is deliberately shaped to make future Datadog APM/DBM instrumentation cheap without adding any yet. |
