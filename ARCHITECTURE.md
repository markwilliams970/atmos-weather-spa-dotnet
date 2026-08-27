# Architecture

This is a **current-state** reference: what the application actually looks like today and why, in one place. For the full decision-by-decision history — including options that were considered and rejected — see [`docs/phase-a-assessment.md`](./docs/phase-a-assessment.md) (assessment of the original TypeScript app) and [`docs/phase-b-target-architecture.md`](./docs/phase-b-target-architecture.md) (the target design this was built from). This document doesn't repeat their reasoning; it summarizes where it landed.

See [`README.md`](./README.md) for the higher-level orientation (purpose, prerequisites, a plain diagram) and the ".NET Core vs. classic .NET" primer if any of the terminology below is unfamiliar.

---

## 1. Shape: hybrid server-rendered, not an SPA

Razor Pages render HTML; a small Minimal API surface serves JSON to browser `fetch()` calls; plain ES modules (no framework, no bundler) own client-local interactivity. This was a deliberate choice over a SPA framework — the original app's UX doesn't need client-side routing or complex state management, and CLAUDE.md's own operating rules explicitly rule out introducing a frontend framework "merely because modular JavaScript is being introduced."

| Concern | Owner |
|---|---|
| HTML structure, routing, layout | Razor Pages (`Pages/`) |
| JSON API (`/api/*`) | Minimal API (`Endpoints/`) — not MVC controllers, not Razor Page handlers; seven small endpoints didn't justify controller-class ceremony |
| Interactivity, map rendering, charts, animations | Browser JavaScript (`wwwroot/js/`) |

## 2. Project layering

```
Atmos.Core   →  shared by both executables below; domain models, WMO codes,
                unit conversions, IGeocodingService/IWeatherService
     ↑                              ↑
Atmos.Web                     Atmos.Cli
(the web app)                 (standalone console tool)
```

`Atmos.Core` exists because two executables concretely need the same code — the one justification this project's own rules (CLAUDE.md §11/§26) accept for pulling shared code into its own project. Web-only concerns (session handling, EF Core, the four "enhancement" services — elevation, nearby-place, air quality, radar) stay in `Atmos.Web` and are never pulled into `Atmos.Core`.

## 3. Domain model mapping — external DTOs never leak

Every external API response is mapped through an explicit chain before anything else in the app touches it:

```
External API DTO  →  Application/domain model  →  Presentation model (if different)
```

`Atmos.Core.Services.WeatherService`'s `ForecastMapper` (internal, its own fixture-tested class) is the canonical example: Open-Meteo's JSON shape is mapped into `WeatherForecast`/`HourlySlot`/`DailyRow` — application models with unit-converted, WMO-decoded, already-rounded values — and the Minimal API endpoint returns *that*, never the raw Open-Meteo response. No `dynamic`, no raw `JsonDocument`/`object` passed between layers.

## 4. Persistence

One entity, `RecentSearch`, via EF Core against SQL Server:

- **No repository layer.** `AtmosDbContext` is injected directly into `RecentSearchService` — wrapping EF Core in another interface would be indirection with no payoff for this shape of app (CLAUDE.md §11).
- **Session-scoped, not account-scoped.** A random 32-hex-char cookie (`SessionCookieMiddleware`) is the only identity concept in the app — no login, matching the product's actual shape rather than adding auth nobody asked for.
- **Capped at 10 per session**, trimmed via a single set-based `ExecuteDeleteAsync` in the same transaction as the upsert (`RecentSearchService.SaveAsync`) — one round trip, not the reference app's separate delete/insert/trim statements.
- **Migrations only** — no `CREATE TABLE IF NOT EXISTS`-style runtime schema magic (CLAUDE.md §22). `dotnet ef database update` locally; an idempotent generated SQL script (`dotnet ef migrations script --idempotent`) applied via `sqlcmd` for the IIS deployment, where the SDK isn't installed.
- **A known gap in the original app is closed here:** map-selected elevation now persists on `RecentSearch` (`ElevationMeters`), so a re-selected map pick keeps its altitude-corrected forecast.

## 5. External API resilience

Every outbound call is isolated behind a service interface (`IGeocodingService`, `IWeatherService`, `IElevationService`, `INearbyPlaceService`, `IAirQualityService`, `IRadarService`) with resilience shaped by how essential the call is:

- **Core-path calls** (ZIP/city lookup, forecast fetch) get one retry and an explicit timeout via `Microsoft.Extensions.Http.Resilience` — losing these means no forecast at all, so it's worth one retry.
- **Enhancement calls** (elevation, nearby-place naming, air quality) fail fast with **no retry** and degrade the UI gracefully (e.g. "elevation unavailable") rather than blocking the page. A cosmetic feature must never hold the core forecast hostage — this is an explicit, repeated rule (CLAUDE.md §12, §29).

## 6. Session and security model

- `SessionCookieMiddleware` issues/validates a `sid` cookie (`HttpOnly`, `SameSite=Lax`, `Secure` when HTTPS) before any handler runs — no ASP.NET Core `ISession` state bag involved, since the app has nothing to store there beyond the correlation key.
- The one mutating endpoint (`PUT /api/recent/units`) is protected by a lightweight same-origin `Origin` header check rather than the full ASP.NET Core antiforgery-token flow — a deliberate, documented proportionality call (Phase B §15/§20): the worst case of a successful cross-origin attack here is flipping one victim's unit preference for one saved label.
- Session IDs are never logged in full — `Infrastructure/SessionLogging.Correlator` produces a one-way, truncated SHA-256 correlator instead, enough to trace one visitor's actions through logs without the log file ever containing the actual cookie value.

## 7. Error handling

`ApiExceptionHandler` (an `IExceptionHandler`) maps exceptions to HTTP status codes for the `/api/*` surface specifically, distinguishing invalid input (400), a known-unavailable upstream (502, `WeatherServiceException`), and genuinely unexpected failures (500) — never forwarding a raw third-party exception message to the client. Page requests fall through to the standard `/Error` page.

One real bug this surfaced during integration testing (D16): `UseExceptionHandler("/Error")` rewrites `HttpContext.Request.Path` to the fallback path *before* invoking any registered `IExceptionHandler`, so a naive `Request.Path.StartsWithSegments("/api")` check never actually matched — every unhandled `/api/*` exception was silently falling through to a generic 500. Fixed by reading the original path from `IExceptionHandlerPathFeature` instead. Worth knowing if this handler is ever touched again.

## 8. Logging and observability

Serilog, config-driven from `appsettings.json`'s `Serilog` section, with two sinks always active: human-readable console, and a JSON file (`Serilog.Formatting.Compact`) meant for a log-collection agent. `Serilog.Enrichers.Span` stamps every log line with the `TraceId`/`SpanId` from .NET's own built-in `System.Diagnostics.Activity` — **not** Datadog-specific, already true with zero APM tooling installed. Logging is deliberately placed at the boundaries that become **spans** once a tracer is attached (one line per HTTP request via `UseSerilogRequestLogging()`, enriched with business context via `IDiagnosticContext` rather than a second log line; every external API call times and logs its own outcome) — not everywhere, and not nowhere. Full design and the explicit "not done yet" boundary (no Datadog package, no exporters, no manual spans) in [`docs/logging.md`](./docs/logging.md).

## 9. Frontend module structure

`wwwroot/js/` — nine plain ES modules, no bundler, no build step:

| Module | Responsibility |
|---|---|
| `app.js` | Bootstrap: wires tab/unit/nav click handlers, imports the others, initial page load |
| `state.js` | Shared app state object, `$()` DOM helper, status-message helpers |
| `search.js` | Search input, debounced autocomplete dropdown |
| `weather.js` | Core orchestration: fetch, populate the UI, unit switching, recent-search rendering, History-API navigation |
| `map-picker.js` | The interactive pan/zoom/click location picker overlay |
| `radar.js` | Radar tile rendering (host+path frame URL, never a raw timestamp — a documented prior-incident fix carried forward from the original app) |
| `charts.js` | SVG gauges (humidity/UV/wind) and the temperature/precipitation charts |
| `themes.js` | Dynamic sky themes (day/night, condition-based animated layers) |
| `geo.js` | Web Mercator tile math shared by the map picker and radar |

`radar.js` and `map-picker.js` deliberately keep two separate tile-rendering loops rather than sharing one, per the original app's own documented rationale (Phase B §18) — they render at different zoom levels for different purposes.

## 10. Testing architecture

Four layers, fastest/most-isolated first — see [`README.md`](./README.md#testing) for the full breakdown and current counts. The integration layer (`WebApplicationFactory<Program>`) and browser layer (Playwright against a real Kestrel listener) both swap SQL Server for EF Core's SQLite provider and every external service for a deterministic fake, sharing that swap logic (`Atmos.Web.Tests/Integration/TestHostConfiguration.cs`) so the two suites can't drift apart. Getting Playwright a real, socket-listening instance required extracting `Program.cs`'s app-assembly logic into a reusable `Program.BuildApp(args, configure)` — `WebApplicationFactory`'s usual trick for this doesn't work for a minimal-hosting-API `Program.cs` like this one (confirmed empirically: a second `Build()` reuses disposed state from `HostFactoryResolver`'s probing invocation).

## 11. Deployment architecture

```
Internet / LAN → IIS (ANCM, in-process hosting) → ASP.NET Core (Kestrel, in-process) → SQL Server
                                                                      ↘ External weather APIs
```

Framework-dependent deployment (not self-contained) — relies on the Hosting Bundle's shared runtime rather than bundling the whole .NET runtime per deployment. The connection-string secret lives in `web.config`'s environment variables, never a committed file (see [`docs/logging.md`](./docs/logging.md)'s deployment note for the incident that established this pattern). Full deployment mechanics in [`DEPLOYMENT.md`](./DEPLOYMENT.md) and [`docs/manual-deployment-walkthrough.md`](./docs/manual-deployment-walkthrough.md).
