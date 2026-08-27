# Logging — design and APM/DBM instrumentation readiness

**Status: this is a logging pass, not an instrumentation pass.** No Datadog package, no `DD_*` environment variable, and no APM/DBM exporter exists anywhere in this codebase. Everything below uses only .NET's own built-in primitives (`System.Diagnostics.Activity`) plus Serilog, a general-purpose structured logging library — not anything Datadog-specific. The goal is that when Datadog APM/DBM instrumentation *is* added later (a separate, deliberate task), it drops onto an already-well-shaped foundation rather than requiring a second pass through every service to add logging from scratch.

---

## What changed

- **Serilog** replaces the built-in `Microsoft.Extensions.Logging` console provider as the app's logging pipeline (`Program.cs`, the standard two-stage bootstrap-logger pattern from `serilog-aspnetcore`).
- **Two sinks, always:** Console (human-readable, for `dotnet run`/IIS stdout) and a **JSON file** (`Serilog.Formatting.Compact.CompactJsonFormatter`) — the artifact meant for a log-collection agent (Datadog's or otherwise) to tail later.
- **The whole pipeline is config-driven**, read from the `Serilog` section of `appsettings.json` via `Serilog.Settings.Configuration` — sinks, levels, and enrichers are declared in JSON, not C#.

## The deployment switch

The file sink's path is the one thing that differs between environments — everything else in `appsettings.Production.json` intentionally repeats the base config verbatim (Serilog's config-layering merges arrays like `WriteTo` by index, which gets confusing fast across files; full redefinition per environment is the safer, more explicit pattern here).

| Environment | File sink path |
|---|---|
| Development (default, this machine) | `logs/atmos-.log` (relative to the app's content root) |
| Production (`ASPNETCORE_ENVIRONMENT=Production`, the IIS/Windows Server deployment) | `C:\ProgramData\atmos\logs\atmos-.log` |

`ASPNETCORE_ENVIRONMENT=Production` is already set via `web.config`'s `<aspNetCore><environmentVariables>` on the deployed IIS site (Phase C) — no new deployment-time configuration was needed to make the switch take effect, only the new `appsettings.Production.json` file. Verified live on the `win2025app` VM (see below): the deployed app pool identity needs write access to `C:\ProgramData\atmos\logs`, granted via `icacls` at deploy time, same as the existing `logs\` folder under the site's own directory (Phase C).

Rolling: daily files, JSON per line, `shared: true` (safe if IIS briefly runs two app-pool worker processes during a recycle). Retention: 14 days in dev, 30 in Production.

## What "ready for trace/span correlation" means today, concretely

.NET's ASP.NET Core hosting stack creates a `System.Diagnostics.Activity` for every incoming request automatically — this has been true for years and has nothing to do with Datadog or OpenTelemetry; it's a core BCL primitive both of those tracing systems are built on top of. `Serilog.Enrichers.Span` (a small, general-purpose package, not a Datadog package) reads `Activity.Current` and stamps every log event with `TraceId`/`SpanId`/`ParentId`.

The practical effect, already true right now with zero APM tooling installed: every log line emitted while handling one HTTP request — across every service it touches, every EF Core command it runs, every outbound HTTP call it makes — shares the same `TraceId`. You can already reconstruct "everything that happened for this one request" from the raw JSON log file today, by grep-ing one `TraceId`.

When Datadog's .NET tracer is added later (out of scope here), it hooks into this exact same `Activity` pipeline (for modern .NET, dd-trace-dotnet's automatic instrumentation is `Activity`-based) and, separately, Datadog's automatic log-injection feature (`DD_LOGS_INJECTION=true`) populates Serilog's `LogContext` with `dd.trace_id`/`dd.span_id`/`dd.env`/`dd.service`/`dd.version` with **no code change required** — this app already reads `Enrich.FromLogContext()`, which is the exact integration point Datadog's instrumentation targets. That's the specific reason Serilog was chosen over hand-rolling a file logger: it's the path of least resistance to correlated logs once real tracing exists.

## What gets logged, and why those boundaries specifically

The principle: log at the boundaries that become **spans** once a tracer is attached — external HTTP calls, database-adjacent business operations, and one rich "root span" summary per request — not everything, and not nothing.

- **One structured line per HTTP request**, from `UseSerilogRequestLogging()` (method, path, status, elapsed) — this is the closest thing to a root-span summary the app has pre-instrumentation. Individual endpoints (`WeatherEndpoints` especially) enrich *that same line* via `IDiagnosticContext.Set(...)` with business context (`Zip`, `Latitude`/`Longitude`, `LocationTypeResolved`, `Condition`, `UsAqi`, etc.) rather than writing a second, separate "handling request" log — one row per request, richly tagged, is the shape a trace-search UI (Datadog's included) actually wants.
- **Every external API call** (`GeocodingService`, `WeatherService`, `ElevationService`, `NearbyPlaceService`, `AirQualityService`, `RadarService`) times itself with a `Stopwatch` and logs the outcome — `Debug` on success (with the resolved value: city name, elevation, AQI, condition), `Information` for a non-2xx response, `Warning` on a thrown exception. This mirrors what an HTTP-client span's duration/status tags would show, but is visible in plain log search today.
- **`NearbyPlaceService`'s Overpass→Nominatim fallback** logs which source actually resolved the name — "primary empty, fell back" is a meaningfully different case from "primary failed" when read as a sequence, and is exactly the kind of thing that's easy to lose once it's "just a span" without a matching log line explaining the business reason.
- **`RecentSearchService.SaveAsync`/`UpdateUnitsAsync`** log one `Information` line per operation (insert vs. update, rows trimmed, matched vs. not) — the individual SQL statements underneath already get logged via EF Core's own provider (see below), so this line exists specifically to answer "why" rather than "what SQL ran."
- **`SessionCookieMiddleware`** logs once per *new* session created (not per request) — a "sessions started" business signal, not per-request noise.
- **The one CSRF-relevant rejection** (`PUT /api/recent/units` from a non-matching `Origin`) logs at `Warning` — low-impact per Phase B §15/§20, but a security-relevant event worth keeping visible.

## Session IDs are never logged in full

CLAUDE.md §14: don't log session identifiers without a compelling reason. `Infrastructure/SessionLogging.Correlator(sessionId)` produces a 12-character SHA-256-truncated, one-way correlator instead — enough to trace one visitor's actions through the logs without the log file ever containing the actual `sid` cookie value.

## EF Core log-level tuning

EF Core's `Debug`-level query-compilation logging (`Microsoft.EntityFrameworkCore.Query`, `ChangeTracking`, etc.) dumps entire LINQ expression trees and generated execution plans — multi-kilobyte single log lines, discovered by actually inspecting the output during this work (one `/api/weather` request produced ~53KB of log before tuning, ~11KB after). `MinimumLevel.Override["Microsoft.EntityFrameworkCore"] = "Information"` keeps the useful `Executed DbCommand (...)ms [...]` lines — which is what DBM-style query visibility actually wants — and drops the plan-compilation noise. Same treatment for `Polly` (the resilience/retry library wrapping the core-path `HttpClient`s): `Information` keeps the one-line-per-attempt result/duration summary, drops the wrapper "pipeline executing/executed" pair around it.

## Deployment note: this made the connection string secret homeless — fixed by moving it to an environment variable

Phase C's original deployment put the VM's real SQL Server connection string directly into a **server-only** `appsettings.Production.json` (never committed, hand-created via WinRM). This pass added a **git-tracked** `appsettings.Production.json` (Serilog config only, no secret) — redeploying it overwrote the server's file and, with it, the connection string. Verifying the deployment switch on the real VM caught this immediately (`/healthz` → 500, `ConnectionString property has not been initialized`).

The fix, applied on the VM and worth carrying into any future deployment process: the connection string now lives in `web.config`'s `<aspNetCore><environmentVariables>` as `ConnectionStrings__AtmosDb` (double underscore = ASP.NET Core's config section separator for environment variables), right alongside the existing `ASPNETCORE_ENVIRONMENT` entry. This is the correct pattern going forward, not a workaround: `appsettings.Production.json` can now be freely committed and redeployed with real, non-secret content (log paths, levels), because the one thing that's actually a secret never lives in a file at all. The one thing to remember: because `dotnet publish` regenerates `web.config` from scratch every time, both environment variables must be re-applied as an explicit deployment step (same as Phase C already required for `ASPNETCORE_ENVIRONMENT` alone) — there is no persistent, un-overwritable place on this VM for them yet. A real CI/CD pipeline would template this properly; for now, redeploying means re-adding both `<environmentVariable>` entries to `web.config`.

## Explicitly not done (by design, this pass)

- No `Datadog.Trace` (or any APM vendor) package.
- No `DD_*`/OpenTelemetry environment variables or exporters.
- No manual span creation (`Activity.StartActivity(...)`) — today's correlation rides entirely on the `Activity` ASP.NET Core already creates per request; introducing custom spans is instrumentation, which is explicitly the next, separate task.
- No DBM-specific SQL comment injection (`SET CONTEXT_INFO`/query-comment propagation) — that's a tracer feature (`DD_DBM_PROPAGATION_MODE`), not something to hand-roll here.
