# Phase B — Target Architecture / Detailed Porting Plan

**Subject:** Detailed porting plan for Atmos Weather → ASP.NET Core 10 / Razor Pages / SQL Server
**Builds on:** `docs/phase-a-assessment.md` (Phase 0/A, complete, decisions resolved 2026-08-27)
**Status:** Draft for review. No implementation performed. No solution/`.csproj`/migrations/C#/Razor files created yet, per CLAUDE.md's Phase B constraints.

This plan incorporates all six Phase A decisions as binding constraints, not options to revisit:
1. Search input lives in the shared layout.
2. `/map` is a secondary deep-link entry point; the picker stays a same-page JS overlay.
3. RainViewer frame lookup moves server-side, behind `IRadarService`.
4. `Atmos.Cli` is retained.
5. `RecentSearch` includes `LocationType`.
6. An early IIS/SQL Server deployment rehearsal ("Stage C+") precedes feature work.

Five new, narrower open questions surfaced while designing this plan (not revisions of the Phase A six) are called out inline and collected in §20.

---

## 1. .NET Version and Baseline Tooling

- **Target framework:** `net10.0`, ASP.NET Core 10, C# with nullable reference types and implicit usings enabled solution-wide.
- **SDK-style projects** throughout; no `packages.config`, no non-SDK legacy project format.
- **EF Core 10** (aligned to the .NET 10 wave) with the SQL Server provider (`Microsoft.EntityFrameworkCore.SqlServer`) and, for fast dependency-free CI, the SQLite provider used only in the test project (§16).
- **dotnet-ef** as a local tool (`dotnet tool install --local dotnet-ef`), pinned in a `.config/dotnet-tools.json` manifest committed to the repo — avoids relying on a global install matching the right version.

---

## 2. Solution and Project Structure

CLAUDE.md's original sketch (§6) shows two projects (`Atmos.Web`, `Atmos.Tests`). Decision #4 (retain the CLI, sharing services rather than duplicating them per CLAUDE.md §35) is a concrete architectural reason to introduce **one** additional project — the minimum needed, not a general move toward fragmentation:

```text
Atmos.sln

src/
    Atmos.Core/                 -- shared by Web and Cli; no ASP.NET Core dependency
        Models/                    Location, WeatherForecast, CurrentConditions,
                                    HourlySlot, DailyRow, GeocodeResult, WmoCondition
        Wmo/                       WMO code table + lookup
        Conversions/               temperature, wind speed, precipitation, elevation,
                                    compass-direction helpers
        Services/                  IGeocodingService, IWeatherService + implementations
        ServiceCollectionExtensions.cs   AddAtmosCoreServices(IConfiguration)

    Atmos.Web/
        Pages/                     Index, Weather, Map, About (+ Shared/_Layout, partials)
        Endpoints/                 Minimal-API extension methods: one per resource group
        Services/                  IElevationService, INearbyPlaceService, IAirQualityService,
                                    IRadarService, IRecentSearchService (web-only)
        Data/                      AtmosDbContext, RecentSearch entity + configuration
        Models/                    Request/response DTOs for every endpoint
        Infrastructure/            Session-cookie middleware, exception-handling middleware
        wwwroot/
            css/site.css
            js/{app,search,weather,charts,radar,map-picker,themes,geo}.js
        Program.cs
        appsettings.json / appsettings.Development.json

    Atmos.Cli/
        Program.cs                 References Atmos.Core only — no web/EF dependency
        ConsoleDisplay.cs          Box/format helpers (kept local — presentation, not domain)

tests/
    Atmos.Core.Tests/             Pure unit tests, no ASP.NET Core / DB dependency
    Atmos.Web.Tests/              WebApplicationFactory-based integration tests
    (Atmos.Web.PlaywrightTests/   Created later, at D17 — not part of this Phase B scaffold)
```

**Rationale for the split:** `Atmos.Cli` needs exactly the overlap between the two apps — WMO mapping, unit conversions, ZIP lookup, and forecast fetch/shape — and nothing else (no sessions, no persistence, no radar/AQ/elevation/nearby-place, no autocomplete beyond what `IGeocodingService` already covers). Everything web-only stays in `Atmos.Web`. This keeps `Atmos.Core` a genuinely shared, dependency-light library rather than a speculative "domain project" — it exists because two executables concretely need the same code, which is the one justification CLAUDE.md §11/§26 accept for an abstraction.

**Splitting tests into two projects** (rather than CLAUDE.md's single `Atmos.Tests` sketch) is a minor, low-stakes deviation: pure unit tests (`Atmos.Core.Tests`) have zero ASP.NET Core/DB dependencies and run in milliseconds; integration tests (`Atmos.Web.Tests`) need `WebApplicationFactory` and a database. Keeping them separate keeps the fast suite fast. This does not need Mark's sign-off — it's a test-organization detail, not an architectural one — but is noted for transparency.

**Still explicitly NOT introduced**, reaffirming Phase A §14: no `Atmos.Domain`/`Atmos.Infrastructure` split within the web app, no repository layer over EF Core, no CQRS/MediatR, no additional class libraries beyond `Atmos.Core`.

---

## 3. Razor Pages Architecture

**Pages** (`Atmos.Web/Pages/`):

| Page | Route | Responsibility |
|---|---|---|
| `Index.cshtml` / `.cshtml.cs` | `/` | Landing/search shell. `OnGet` accepts an optional `zip` query param (preserves today's `?zip=` deep-link, `weather-server.ts:2252-2253`) and redirects straight to `/weather?zip=...` if present — matching today's "auto-search on load" behavior. |
| `Weather.cshtml` / `.cshtml.cs` | `/weather` | Renders the hero-card + tab-nav shell (server-rendered skeleton, empty state) exactly like today's initial markup. `OnGet` validates the presence of `zip=` or `lat=&lon=&label=` in the query string; if neither is present, redirects to `/`. Actual data population happens client-side via the JSON API on page load, identical to today's `getWeather()` flow. |
| `Map.cshtml` / `.cshtml.cs` | `/map` | Secondary deep-link entry point per decision #2 — hosts the same picker overlay markup/JS as a full page for someone who navigates here directly (e.g., a bookmark), while the day-to-day trigger remains the overlay launched from the search dropdown on whatever page the user is on. |
| `About.cshtml` | `/about` | Static content: data sources and attribution (Open-Meteo, Zippopotam.us, RainViewer, OpenStreetMap/Overpass, Nominatim, CartoDB) — new content; no equivalent page exists today, only inline attribution strings (`weather-server.ts:1206,1227`). |

**Shared layout** (`Pages/Shared/_Layout.cshtml`): sky-background container, the `.shell` grid (sidebar + main), the search bar + suggestions dropdown (per decision #1 — present on every page, not just `/`), the bottom nav + recents drawer (mobile), and the map-picker overlay markup (per decision #2 — lives in the layout so it's available regardless of which page launched it). `/about` inherits this too; the search bar remains functional there, consistent with "search is always available."

**Razor Pages provide, per CLAUDE.md §7:** server-rendered page structure, routing, the common layout, and error handling (via the exception-handling middleware in §14). They do **not** own the JSON API surface — see below.

**JSON API surface — Minimal APIs, not Razor Page handlers or MVC controllers.** The `/api/*` routes are consumed exclusively by client-side `fetch()`, never by a form post or page navigation — there is no HTML to render for them. Forcing them into Razor Page handler methods (`OnGetWeatherAsync` on a markup-less page) is an awkward fit; a full MVC controller layer is unnecessary machinery for seven small endpoints. ASP.NET Core 10's Minimal API, grouped into per-resource extension methods under `Endpoints/` and mapped from `Program.cs`, is the idiomatic, lowest-ceremony choice and keeps a clean seam: `Pages/` renders HTML, `Endpoints/` returns JSON. This is consistent with CLAUDE.md §16's instruction to prefer conventional HTTP semantics and avoid unnecessary layers — it does not conflict with "prefer Razor Pages," since Razor Pages remains the answer for every actual HTML view.

```text
Endpoints/
    WeatherEndpoints.cs        MapWeatherEndpoints(app)   -> GET /api/weather
    GeocodeEndpoints.cs        MapGeocodeEndpoints(app)   -> GET /api/geocode
    RecentSearchEndpoints.cs   MapRecentEndpoints(app)    -> GET /api/recent, PUT /api/recent/units
    AirQualityEndpoints.cs     MapAirQualityEndpoints(app)-> GET /api/air-quality
    ElevationEndpoints.cs      MapElevationEndpoints(app) -> GET /api/elevation
    NearbyPlaceEndpoints.cs    MapNearbyPlaceEndpoints(app)-> GET /api/nearby-place
    RadarEndpoints.cs          MapRadarEndpoints(app)     -> GET /api/radar/frame   (NEW, decision #3)
```

---

## 4. Page Structure and Navigation Model

Routes finalized per §3. One additional design decision, made here rather than left implicit:

**Navigation model — History API, not full-page reloads, for in-app searches.** CLAUDE.md §7 explicitly lists "dynamic weather refresh" as a JavaScript browser responsibility, meaning a search should not force a full page reload — that would be a UX regression from today's true single-document SPA feel. The recommended model:

- A search (ZIP entry, city selection, recent-item click, or map-picker "Use this location") triggers a client-side `fetch()` to `/api/weather`, exactly as today, and calls `history.pushState()` to update the visible URL to `/weather?...` — no reload, no navigation, matching today's instant-update feel exactly.
- `/weather` is also a fully functional **cold-load entry point**: visiting it directly (bookmark, shared link, browser refresh) server-renders the shell and client JS fetches+populates on `DOMContentLoaded` using the query string, the same code path a `pushState` update already exercises.
- Browser back/forward (`popstate`) re-runs the same fetch+populate for the URL being navigated to.

This is purely additive relative to today's behavior (today only `?zip=` on `/` is link-shareable; every other search result lives only in unaddressable in-page state) and does not require Mark's approval as a gate item — it removes no existing capability. It is called out explicitly here because it's a genuine new piece of client JS architecture (`app.js` needs a small router: read query string → fetch → populate, callable both on load and on `popstate`), not because it's controversial.

---

## 5. JavaScript Structure

CLAUDE.md §31 explicitly lifts the ES5 constraint (it existed only to avoid backtick collisions inside the outer TypeScript template literal — not applicable once the JS is a real file). Recommend plain modern JavaScript as ES modules, **no bundler, no TypeScript-for-browser, no build step** — consistent with the original app's "no build step" ethos and CLAUDE.md §26's anti-overengineering stance; static files are served directly by ASP.NET Core's static-file middleware.

Mapping from the current inline `<script>` block (`weather-server.ts:1276-2254`) to modules:

| Module | Contents (ported from `weather-server.ts` lines) | Notes |
|---|---|---|
| `geo.js` | `xyFromLatLon`, `lonLatFromXY` (1924-1930, 2004-2010) | The one piece of logic genuinely shared between radar and picker, per `Claude.md:337-348`'s own documented rationale — kept as the **only** shared module between them |
| `app.js` | `state`, `$` helper, status helpers (1278-1302), init/URL-param handling (2249-2253), the new History-API router (§4) | Bootstrap only |
| `search.js` | Suggestions/autocomplete, debounce, keyboard nav (1304-1374), `getWeather()` orchestration (1783-1817) | Calls into `weather.js` to render, and into `app.js`'s router to update the URL |
| `weather.js` | `populate()`, `applyUnits()` (1727-1779), unit toggle (1821-1832), tab switching (1834-1863), recent-search render/click (1865-1920), AQ fetch/render (2210-2234) | AQ rendering now trusts server-provided `category`/`color` instead of recomputing (§18 — closes the Phase A §7 duplication finding) |
| `charts.js` | SVG gauges, sun arc, temp chart, hourly/daily cards, AQI bar (1479-1723, 2189-2208) | Pure rendering, ported near-verbatim |
| `radar.js` | `renderRadarMap` (1932-2000) | Frame metadata now comes from `GET /api/radar/frame` (decision #3) instead of a direct `fetch('https://api.rainviewer.com/...')` — tile URL construction (`host+path+/256/z/x/y/...`) is otherwise unchanged, preserving the documented fix for the earlier `time`-based 410 incident |
| `map-picker.js` | `fmtCoordLabel`, `pickerState`, all pan/zoom/drag/click handlers, `openMapPicker`/`closeMapPicker`, "Use this location" flow (2012-2176) | Ported as directly as possible — flagged High risk in Phase A §18; no "improvements" during the port, faithful translation only |
| `themes.js` | `THEMES`, `getThemeKey`, `applyTheme`, `hexToRgb`, `makeStars/makeRain/makeSnow/makeClouds/makeFog` (1377-1478) | Preserve exact `rnd()` ranges and per-theme parameters |

`renderRadarMap` (radar.js) and `renderPickerTiles` (map-picker.js) **remain two separate functions**, not unified into one shared tile-rendering helper — this repeats the original author's own explicit, documented decision (`Claude.md:337-348`: fixed-zoom/dual-layer/one-shot vs. variable-zoom/single-layer/repeated-during-drag) and this assessment agrees the rationale still holds after the port; only the coordinate math (`geo.js`) is actually shared.

---

## 6. CSS Structure

One file, `wwwroot/css/site.css`, containing the custom-property `:root` block, the glass-morphism card pattern, layout grid, and the two breakpoints (720px/420px) — ported from the `<style>` block (`weather-server.ts:399-1025`, ~625 lines). Given the app's size, splitting into multiple CSS files would add navigation overhead without a real maintainability win (CLAUDE.md §26); one file is the appropriately simple choice. Theme colors remain runtime-set CSS custom properties via JS (`themes.js`, unchanged), not separate stylesheets — there was never a CSS-side theme split to begin with.

---

## 7. SQL Schema

Conceptual DDL (no migration created yet, per Phase B constraints):

```sql
CREATE TABLE dbo.RecentSearch (
    Id               INT IDENTITY(1,1)  NOT NULL,
    SessionId        CHAR(32)           NOT NULL,
    Label            NVARCHAR(200)      NOT NULL,
    Latitude         FLOAT              NOT NULL,
    Longitude        FLOAT              NOT NULL,
    ElevationMeters  FLOAT              NULL,
    Units            NVARCHAR(10)       NOT NULL CONSTRAINT DF_RecentSearch_Units DEFAULT ('imperial'),
    LocationType     NVARCHAR(10)       NOT NULL CONSTRAINT DF_RecentSearch_LocationType DEFAULT ('zip'),
    CreatedUtc       DATETIME2(3)       NOT NULL CONSTRAINT DF_RecentSearch_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    LastAccessedUtc  DATETIME2(3)       NOT NULL CONSTRAINT DF_RecentSearch_LastAccessedUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_RecentSearch PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_RecentSearch_Session_Label UNIQUE (SessionId, Label),
    CONSTRAINT CK_RecentSearch_Units CHECK (Units IN ('imperial','metric')),
    CONSTRAINT CK_RecentSearch_LocationType CHECK (LocationType IN ('zip','city','map')),
    CONSTRAINT CK_RecentSearch_Latitude CHECK (Latitude BETWEEN -90 AND 90),
    CONSTRAINT CK_RecentSearch_Longitude CHECK (Longitude BETWEEN -180 AND 180)
);

CREATE NONCLUSTERED INDEX IX_RecentSearch_SessionId_LastAccessedUtc
    ON dbo.RecentSearch (SessionId, LastAccessedUtc DESC)
    INCLUDE (Label, Latitude, Longitude, ElevationMeters, Units, LocationType);
```

**Trim-to-10 as a single set-based operation** (replacing the original's three sequential statements, `weather-server.ts:27-31`):

```sql
DELETE rs
FROM dbo.RecentSearch rs
WHERE rs.SessionId = @SessionId
  AND rs.Id NOT IN (
      SELECT TOP (10) Id FROM dbo.RecentSearch
      WHERE SessionId = @SessionId
      ORDER BY LastAccessedUtc DESC
  );
```

In EF Core this becomes `RecentSearches.Where(...).OrderBy(...).Skip(10).ExecuteDeleteAsync()` (EF Core bulk `ExecuteDelete`, no entity materialization) — run inside the same transaction as the upsert to avoid any window where a trim could race a concurrent save for the same session.

A unique constraint on `(SessionId, Label)` replaces the original's delete-before-insert emulation of uniqueness — the upsert becomes a genuine `if exists → update else insert`, matching EF Core idioms directly.

---

## 8. EF Core Model

```csharp
// Atmos.Web/Data/RecentSearch.cs
public sealed class RecentSearch
{
    public int Id { get; set; }
    public required string SessionId { get; set; }
    public required string Label { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? ElevationMeters { get; set; }
    public UnitsPreference Units { get; set; } = UnitsPreference.Imperial;
    public LocationType LocationType { get; set; } = LocationType.Zip;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastAccessedUtc { get; set; }
}

public enum UnitsPreference { Imperial, Metric }
public enum LocationType { Zip, City, Map }
```

`UnitsPreference`/`LocationType` are C# enums mapped to the narrow string columns via `.HasConversion<string>()` in `IEntityTypeConfiguration<RecentSearch>` — gives compile-time safety in application code while keeping the stored values human-readable in SQL Server (matches today's plain-string DB values). `AtmosDbContext` exposes `DbSet<RecentSearch> RecentSearches`; entity configuration (keys, indexes, check constraints via `.ToTable(t => t.HasCheckConstraint(...))`, default-value SQL) lives in `Data/Configurations/RecentSearchConfiguration.cs`.

No `Database.Migrate()` call in the Production startup path (see §17) — migrations are an explicit deployment step, per CLAUDE.md §22's prohibition on implicit runtime schema manipulation.

---

## 9. Service Boundaries

| Interface | Project | Backing implementation | Lifetime |
|---|---|---|---|
| `IGeocodingService` | Atmos.Core | Zippopotam (`LookupZipAsync`) + Open-Meteo geocoding (`SearchCityAsync`) via typed `HttpClient` | Scoped (typed-client default) |
| `IWeatherService` | Atmos.Core | Open-Meteo forecast, optionally elevation-corrected; owns the hour-window/daily-labeling shaping logic (the single most complex logic in the original app, per Phase A §7) | Scoped |
| `IElevationService` | Atmos.Web | Open-Meteo Elevation API | Scoped |
| `INearbyPlaceService` | Atmos.Web | Overpass → Nominatim fallback, preserving the "fail fast, no retry, no radius widening" policy verbatim | Scoped |
| `IAirQualityService` | Atmos.Web | Open-Meteo Air Quality API; owns AQI categorization (now the *only* place this logic exists — §18) | Scoped |
| `IRadarService` | Atmos.Web | **New** — RainViewer frame-metadata lookup, moved server-side per decision #3 | Scoped |
| `IRecentSearchService` | Atmos.Web | Upsert-and-trim against `AtmosDbContext`; not a repository — it contains real logic (§14/§11 of CLAUDE.md's distinction between a legitimate service and a repository-over-EF-Core anti-pattern) | Scoped |
| Session-cookie handling | Atmos.Web | A small `SessionCookieMiddleware` (issues/validates the `sid` cookie once per request, mirrors `getOrCreateSession()`) + a scoped `IAppSessionAccessor` reading the resolved id from `HttpContext.Items` for handlers/endpoints to consume | Middleware + Scoped accessor |

No interface is introduced for `AtmosDbContext` itself, and no repository wraps `IRecentSearchService`'s EF Core calls — both directly follow CLAUDE.md §11's instruction not to wrap an abstraction (EF Core) in another abstraction without a concrete testability/substitution need that EF Core's own `DbContext` test doubles don't already satisfy.

**Registration:** `Atmos.Core` exposes `IServiceCollection.AddAtmosCoreServices(IConfiguration)`, called from both `Atmos.Web/Program.cs` and `Atmos.Cli/Program.cs` — the one piece of composition-root code that's genuinely shared, avoiding each host re-declaring the same `AddHttpClient<...>()` registrations.

---

## 10. HTTP Client Design and External API Resilience

Every external call goes through a typed `HttpClient` (`AddHttpClient<TInterface, TImpl>()`), each with its **own** explicit timeout rather than one global default:

| Client | Timeout | Retry | Cancellation |
|---|---|---|---|
| Zippopotam (`IGeocodingService.LookupZipAsync`) | 8s | 1 retry, ~300ms backoff (core path) | `HttpContext.RequestAborted` propagated |
| Open-Meteo Geocoding (`SearchCityAsync`) | 8s | None (autocomplete — already non-fatal by design, matches today's `catch → {results:[]}`) | Propagated |
| Open-Meteo Forecast (`IWeatherService`) | 8s | 1 retry, ~300ms backoff (core path) | Propagated |
| Open-Meteo Air Quality | 8s | None | Propagated |
| Open-Meteo Elevation | 6s | None (cosmetic/enhancement path) | Propagated |
| Overpass (`INearbyPlaceService`) | 6s | None | Propagated (preserves the original 6s value exactly) |
| Nominatim (fallback) | 5s | None | Propagated (preserves the original 5s value exactly) |
| RainViewer frame metadata (`IRadarService`, new server-side call) | 6s | None | Propagated |

This closes the Phase A §9 finding (5 of 7 calls had no timeout at all) while deliberately **not** adding retries to the cosmetic/enhancement-only calls — preserving the original's explicit "fail fast" product decision (`Claude.md:107-108, 418-427`) rather than "fixing" it into something more thorough than intended. No circuit breaker is introduced — unnecessary machinery at this traffic scale (CLAUDE.md §26).

---

## 11. Client/Server Contract

Explicit C# record DTOs for every endpoint, closing Phase A §6's implicit-contract finding:

```text
GET  /api/weather        -> WeatherResponse        (mirrors WeatherForecast domain model)
GET  /api/geocode        -> GeocodeResponse { IReadOnlyList<GeocodeResult> Results }
                             GeocodeResult { Name, Admin1?, CountryCode?, Latitude, Longitude }
                             -- explicit translation of Open-Meteo's raw shape; closes the
                                Phase A §4 hidden-coupling finding (today's pass-through)
GET  /api/recent         -> IReadOnlyList<RecentSearchResponse>
                             { Label, Latitude, Longitude, ElevationMeters?, Units }
PUT  /api/recent/units   -> body RecentSearchUnitsRequest { Label, Units } -> 204 No Content
                             -- replaces GET /api/save-units per CLAUDE.md §16
GET  /api/air-quality    -> AirQualityResponse { UsAqi, Pm25, Pm10, Ozone, No2, Category, Color }
GET  /api/elevation      -> ElevationResponse { double? Elevation }
GET  /api/nearby-place   -> NearbyPlaceResponse { string? Name }
GET  /api/radar/frame    -> RadarFrameResponse { Host, Path, FrameTimeUtc }      -- NEW (decision #3)

Errors (all endpoints) -> ApiErrorResponse { string Error }
```

`ApiErrorResponse` deliberately keeps today's exact `{error: "..."}` field name/shape — a free compatibility choice that means the ported client JS's error-handling (`err.message` reads) needs no logic change, only a call-site rename.

**Verb choice for `PUT /api/recent/units`:** PUT over POST, since the operation is an idempotent replace of a known resource's (a label's) unit preference — POST would also be defensible; PUT is the marginally better semantic fit and is the recommendation, not a hard requirement.

---

## 12. Configuration

```json
{
  "ConnectionStrings": {
    "AtmosDb": ""
  },
  "ExternalApis": {
    "Zippopotam": "https://api.zippopotam.us",
    "OpenMeteoForecast": "https://api.open-meteo.com",
    "OpenMeteoGeocoding": "https://geocoding-api.open-meteo.com",
    "OpenMeteoAirQuality": "https://air-quality-api.open-meteo.com",
    "Overpass": "https://overpass-api.de",
    "Nominatim": "https://nominatim.openstreetmap.org",
    "NominatimUserAgent": "AtmosWeather/1.0 (+https://github.com/markwilliams970/atmos-weather-spa-dotnet)",
    "RainViewer": "https://api.rainviewer.com"
  },
  "RecentSearch": { "MaxPerSession": 10 },
  "SessionCookie": { "Name": "sid", "MaxAgeDays": 365 }
}
```

Bound to strongly-typed `Options` classes (`ExternalApiOptions`, `RecentSearchOptions`, `SessionCookieOptions`) via `Configure<T>()` — no hardcoded literals anywhere in `Atmos.Core`/`Atmos.Web` source, per CLAUDE.md §13. `ConnectionStrings:AtmosDb` is empty in source control, everywhere, always: supplied locally via .NET User Secrets (`dotnet user-secrets`, outside the repo entirely) and on the Windows Server 2025 VM via a `ConnectionStrings__AtmosDb` environment variable in `web.config` — never through any `appsettings.*.json` file, including environment-specific ones, since those are now git-tracked, redeployable content (see [`docs/logging.md`](./logging.md)'s deployment note for why this specific line was tightened after `appsettings.Production.json` became a real file with real content).

**Decision:** `NominatimUserAgent` replaces the old placeholder (`"Atmos-Weather-Demo/1.0 (learning project)"`, `weather-server.ts:373`) with `AtmosWeather/1.0 (+https://github.com/markwilliams970/atmos-weather-spa-dotnet)` — identifies the app via its public repo link, satisfying Nominatim's usage policy without publishing personal contact info in a committed config file. This is a header the *server* sends on its own outbound call to Nominatim (`INearbyPlaceService`'s fallback), unrelated to the browser.

---

## 13. Logging and Observability

Structured logging (message templates, not interpolated strings), covering every category CLAUDE.md §14 requires: startup, configuration failures, external API failures (service name + status/exception type, never the raw response body), unhandled exceptions (via the exception-handling middleware, §14), database failures, and key application events (session created, recent-search saved).

**Explicitly not logged** at Information level or above: session IDs (CLAUDE.md's own carve-out — a truncated id at Debug level is acceptable for support scenarios, not the full value by default), secrets (none exist today, but the policy holds for future API keys), and free-text `Label` values (user-influenced; log at Debug only, not Information).

**The concrete fix for Phase A's "zero trace for debugging" finding:** every currently-silent server-side `catch` (nearby-place, radar frame lookup) gets a `LogWarning` with the service name and failure reason — the user-facing graceful-degradation behavior is unchanged, but a production failure is no longer invisible. Client-side silent catches (`loadRecent`, geocode-suggestion fetch) remain silent in the browser, since browser telemetry is out of scope here.

**Sink — superseded during Phase D, see [`docs/logging.md`](./logging.md).** This section originally recommended the built-in console/debug provider, captured through IIS's stdout log redirection, and explicitly recommended *against* adding Serilog "unless file-based structured logging becomes a real operational need." That need materialized directly: Mark's stated intent to add Datadog APM/DBM instrumentation as a later learning exercise means logs need to be structured, JSON-capable, and trace/span-correlation-ready *now*, in preparation. Serilog (Console + JSON file sinks, config-driven per environment, `Serilog.Enrichers.Span` for `Activity`-based `TraceId`/`SpanId` correlation) was adopted during Phase D specifically for this reason — not a reversal of the "avoid unnecessary dependencies" principle CLAUDE.md §26 still holds elsewhere, but a case where the dependency is directly justified by an explicit, stated future requirement. `docs/logging.md` has the full design, what's deliberately *not* done (no Datadog package, no instrumentation), and a real deployment gotcha this surfaced (the connection string secret needed to move out of `appsettings.Production.json` and into a `web.config` environment variable once that file became git-tracked, redeployable content).

---

## 14. Error Handling

A global exception-handling middleware converts unhandled exceptions into a generic response — `ApiErrorResponse` (JSON) for `/api/*` requests, a friendly error page for HTML page requests — and never leaks exception messages or stack traces to the client. This directly closes the Phase A §9/§15 finding that today's handlers forward `(err as Error).message` verbatim.

Implements CLAUDE.md §15's four-way distinction concretely:

| Category | Example | Response |
|---|---|---|
| Invalid user input | Malformed ZIP, non-numeric lat/lon, label too long | 400, specific actionable message (same validation branches as today) |
| Unavailable core weather data | Open-Meteo/Zippopotam failure/timeout | 502/503, generic "Weather data is temporarily unavailable" — **not** the raw upstream exception text (the one deliberate behavior change from today) |
| Unavailable optional enhancement | Elevation/nearby-place/radar/AQ failure | The relevant service returns `null`/an "unavailable" field; the endpoint still returns 200 with a partial payload — formalizes today's already-correct graceful-degradation pattern, no change to user-visible behavior |
| Unexpected application error | Anything uncaught | 500, generic message; full detail only in server logs |

---

## 15. Security

Concrete implementation closing every Phase A §8 finding, per CLAUDE.md §17's baseline list:

- **HTTPS:** `UseHttpsRedirection()` + HSTS in Production; IIS binding configured at deployment (§17/Phase E).
- **Session cookie:** `HttpOnly=true`, `Secure=true` in Production (relaxed for local HTTP dev), `SameSite=Lax`, configurable `MaxAge` (default 365 days) — same shape as today, `Secure` added.
- **Anti-forgery for `PUT /api/recent/units`:** the only real state-changing endpoint, called via same-origin `fetch()`. **Decision:** a same-origin check (verify `Origin`/`Referer` against the app's own host), not the full ASP.NET Core antiforgery-token flow — approved as proportionate to the low real-world impact (an attacker flips one victim's saved unit preference for one label — Phase A §8's own characterization).
- **Input validation:** ZIP regex, lat/lon range + `NaN` checks, `Label` max length (200, new — closes the Phase A §5 gap where the original had no bound at all), `Units`/`LocationType` restricted to their enum values — enforced at the minimal-API handler level, mirroring today's inline checks.
- **Output encoding:** Razor's automatic encoding for any server-rendered value; client JS keeps using `textContent` everywhere it does today, and a single tested `escapeHtml()` helper (ported from `escHtml()`) centralized in `weather.js` for the one `innerHTML` usage (the recent-item list) — same pattern, no longer inline-duplicated.
- **SQL access:** 100% EF Core LINQ — no raw SQL string concatenation anywhere, preserving today's clean record.
- **Nominatim User-Agent:** updated to `AtmosWeather/1.0 (+https://github.com/markwilliams970/atmos-weather-spa-dotnet)` (§12).

---

## 16. Testing Strategy

| Layer | Project | Covers | Approach |
|---|---|---|---|
| Unit | `Atmos.Core.Tests` (xUnit) | WMO lookup+fallback, all unit conversions, compass conversion, haversine, AQI categorization (now consolidated server-only), forecast-shaping (hour-window selection, daily labeling) against golden-file fixture JSON captured from real Open-Meteo responses, `IGeocodingService`/`IWeatherService` against a fake `HttpMessageHandler` — zero live network | No ASP.NET Core, no DB — fast |
| Integration | `Atmos.Web.Tests` (`WebApplicationFactory<Program>`) | Routing for all 4 pages + all 7 API endpoints, invalid-input 400s, session cookie issuance/reuse, recent-search upsert+trim end-to-end, unit-preference update via the new `PUT` endpoint, same-origin-check behavior | EF Core against the SQLite in-memory relational provider for fast CI runs; recommend a periodic/pre-deployment manual run against real SQL Server to catch any provider-specific SQL differences — a small, explicitly-acknowledged fidelity gap, not a blind spot |
| Browser | `Atmos.Web.PlaywrightTests` (deferred to D17) | ZIP search → autocomplete → forecast render → recent selection → unit switch → map picker → map-selected forecast → radar, per Phase A's existing priority order | Not created in this phase |

**Decision:** no GitHub Actions CI workflow at this time. `dotnet test` (both projects) remains a manual/local step; revisit later if desired.

---

## 17. Build and Deployment Strategy

Finalizes Phase A §17/§19 with decision #6's Stage C+ made concrete:

- **Stage C+ (walking skeleton, immediately after Phase C, before any real feature work):** a minimal `Atmos.Web` — empty `Index` page, `AtmosDbContext` with the real `RecentSearch` migration applied — published to IIS on the Windows Server 2025 VM, over HTTPS, against real SQL Server. Purpose: prove the ASP.NET Core Hosting Bundle, app pool configuration, SQL Server auth, and HTTPS binding all work *before* any feature work depends on them, rather than discovering environmental problems during Phase E and misdiagnosing them as application bugs.
- **Publish:** `dotnet publish -c Release`, framework-dependent (not self-contained), `web.config` generated by publish, not hand-maintained.
- **App pool:** "No Managed Code," standard ASP.NET Core Module integration.
- **Migrations:** applied as an explicit deployment step (`dotnet ef database update`, or a migration bundle via `dotnet ef migrations bundle` for environments without the SDK installed) — **never** via `Database.Migrate()` on Production startup, which is unsafe under IIS's multi-instance/recycling model. A `Development`-only startup code path may still call it for local convenience.
- **Logging in IIS:** `stdoutLogEnabled` in `web.config` during initial rollout/troubleshooting, pointed at a log folder with correct app-pool-identity write permissions.
- **Smoke test:** matches the existing Phase E checklist in CLAUDE.md §23 verbatim — no changes needed to that list.

---

## 18. Behavior Disposition — Preserved / Improved / Removed / Deferred

Required explicitly by CLAUDE.md §23 for Phase B.

### Preserved (unchanged user-visible behavior)

ZIP/city search; debounced autocomplete; map-picker overlay UX (pan/zoom/drag/click); Current/24-Hour/7-Day tabs; sidebar (desktop) + bottom-nav/drawer (mobile) recents; instant client-side unit toggle; all 12 sky themes and animated layers; radar tile rendering approach (CartoDB dark basemap + RainViewer overlay, `mix-blend-mode: screen`); the `host + path` RainViewer tile-URL construction (not the deprecated raw `time` timestamp — the documented prior-incident fix, `Claude.md:188-197`); elevation-aware forecast for map-picked points; the nearby-place "fail fast, no retry, no radius widening" policy; the AQ-card's decoupled-fetch graceful degradation; the elevation/nearby-place non-blocking warning pattern (forecast never held hostage by cosmetic enhancements); max-10-recent-searches-per-session; radar.js/map-picker.js kept as two separate tile-rendering loops sharing only coordinate math, per the original's own documented rationale.

### Improved (deliberate, justified changes)

- `GET /api/save-units` → `PUT /api/recent/units` (HTTP semantics, CLAUDE.md §16).
- Map-selected elevation now persists on `RecentSearch` (closes the known gap, CLAUDE.md §9).
- RainViewer frame lookup moves server-side behind `IRadarService` (decision #3) — the one external call the original app didn't isolate behind a server-side client now is.
- Explicit timeouts added to all 8 external calls (today: 2 of 7 have any; §10).
- Cancellation-token propagation from the HTTP request lifecycle into every outbound call (today: none).
- One retry, short backoff, on the two core-path calls only (ZIP lookup, forecast) — cosmetic/enhancement calls deliberately keep zero retries, preserving the original's own "fail fast" policy rather than "improving" it into something it was never meant to be.
- AQI category/color consolidated to server-side-only computation; client renders the server-provided fields instead of recomputing them (closes the live duplication found in Phase A §7).
- `/api/geocode`'s response wrapped in an explicit `GeocodeResponse`/`GeocodeResult` DTO instead of Open-Meteo's raw shape passing through (closes the Phase A §4 hidden-coupling finding).
- Structured server-side logging added around every previously-silent server-side catch (client-side silent catches for cosmetic UI fetches are intentionally left as-is).
- Raw upstream exception messages no longer forwarded verbatim to API clients; replaced by the four-category error model (§14).
- `Label` gains a server-enforced 200-character maximum (today: unbounded).
- `sid` cookie gains `Secure` in Production.
- `/weather` becomes a real, bookmarkable/shareable URL via the History API (§4) — today only `?zip=` on `/` is link-shareable.
- `SessionId`/`Units`/`LocationType` become typed/constrained columns (`CHECK` constraints, C# enums) instead of SQLite's free-text `TEXT` columns.
- Recent-search upsert-and-trim becomes one transactional, set-based operation instead of three sequential, non-transactional statements.

### Removed

**Nothing.** No feature identified in the Phase A inventory (§3) is being dropped; every capability CLAUDE.md §27 designates "important" is preserved above.

### Deferred (explicitly out of scope for this port)

- Non-US ZIP/geocoding support (the original app's own unimplemented "Next steps" idea; not requested here).
- Saved "favorite" locations distinct from auto-populated Recent (same — an unimplemented idea in the original, not requested).
- A client-side JSON schema validator at the browser/server boundary — the new explicit C# DTOs (§11) already close most of the practical gap server-side; a browser-side validator adds real complexity for a small residual benefit and isn't requested.
- GitHub Actions CI — declined for now (§16); `dotnet test` stays a manual/local step.
- Full ASP.NET Core antiforgery-token flow for `/api/recent/units` — declined in favor of the lighter same-origin check (§15).

---

## 19. Implementation Sequence (refinement of Phase A §19)

Phase A §19 already established stage-by-stage objectives/dependencies/deliverables/test-requirements/completion-criteria at a conceptual level, including the new Stage C+ deployment rehearsal (decision #6). This section only maps that sequence onto the concrete project structure finalized above — the ordering itself is unchanged from Phase A:

- **C** → prove the toolchain locally.
- **C+** → `Atmos.Web` walking skeleton + real `RecentSearch` migration, deployed to IIS/SQL Server on the VM (§17).
- **D1–D4** → `Atmos.Web/Data` (EF Core + `RecentSearch`), `SessionCookieMiddleware`/`IAppSessionAccessor`, `IRecentSearchService`.
- **D5–D6** → `Atmos.Core` (`IGeocodingService`, `IWeatherService`, WMO/conversions), consumed by both `Atmos.Web` and (later) `Atmos.Cli`.
- **D7** → `Pages/Weather.cshtml` + `Endpoints/WeatherEndpoints.cs`.
- **D8–D10** → `search.js`/`GeocodeEndpoints`, unit toggle + `RecentSearchEndpoints`, `IAirQualityService`/`AirQualityEndpoints`.
- **D11–D12** (High risk, §18 of Phase A) → `map-picker.js`/`Pages/Map.cshtml`, `radar.js`/`IRadarService`/`RadarEndpoints` (the new server-side hop, decision #3).
- **D13–D14** → `themes.js`, responsive verification at both breakpoints.
- **D15** (conditional, now confirmed by decision #4) → `Atmos.Cli`, consuming `Atmos.Core` directly.
- **D16–D17** → `Atmos.Web.Tests`, then `Atmos.Web.PlaywrightTests`.
- **D18** → `README.md`/`ARCHITECTURE.md`/`DEPLOYMENT.md` updated to reflect the final, built state.

---

## 20. Open Questions Requiring Mark's Approval

Narrower than Phase A's six — these surfaced while designing the concrete plan, not revisions of decisions already made:

1. **Nominatim `User-Agent` string** (§12) — what should the new deployment's identifying contact string say, replacing the old `"Atmos-Weather-Demo/1.0 (learning project)"` placeholder?
2. **Anti-forgery approach for `PUT /api/recent/units`** (§15) — same-origin header check (recommended, proportionate to the low real-world impact) vs. the full ASP.NET Core antiforgery-token flow?
3. **GitHub Actions CI** (§16) — add a `dotnet test` workflow now, or defer? Recommended: add it now, low cost.
4. **Confirm `Atmos.Core` as a third project** (§2) — a direct, near-mandatory consequence of decision #4, but CLAUDE.md's Phase B instructions still ask for explicit acknowledgment of any project beyond the original two-project sketch.
5. **History-API/bookmarkable-`/weather`-URL navigation model** (§4) — confirm this additive UX capability (real shareable search-result URLs) is wanted, versus keeping `/weather` a pure client-state shell with no server-addressable result state, more strictly mirroring today's single-document app.

### Decisions (resolved 2026-08-27)

1. **Nominatim `User-Agent` string — pending final value.** Clarified that this is a header the .NET server sends on its own outbound call to Nominatim (`INearbyPlaceService`'s fallback lookup, `weather-server.ts:372-375` today) — it is unrelated to the browser/SPA, and .NET's `HttpClient` sends no default `User-Agent` for a server-to-server call to substitute in. Proposed value: `AtmosWeather/1.0 (+https://github.com/markwilliams970/atmos-weather-spa-dotnet)` — identifies the app via the public repo link rather than a personal email, since `appsettings.json` is committed to a public repo. Awaiting Mark's confirmation of this exact string before Phase C.
2. **Anti-forgery approach — Approved.** Lightweight same-origin header check for `PUT /api/recent/units`, not the full ASP.NET Core antiforgery-token flow.
3. **GitHub Actions CI — Declined for now.** No CI workflow at this time; `dotnet test` remains a manual/local step. Revisit later if desired.
4. **`Atmos.Core` as a third project — Approved.**
5. **Bookmarkable `/weather` navigation model — Approved.**

---

# Phase Gate

```text
Phase: B (Target Architecture)
Status: Complete — draft for review
Completed:
  - .NET version, Razor Pages architecture, page structure, JavaScript
    structure, SQL schema, EF Core model, service boundaries,
    configuration, logging, error handling, security, testing, and
    deployment all specified at a concrete, reviewable level.
  - All six Phase A decisions incorporated as binding constraints.
  - Explicit preserved/improved/removed/deferred disposition produced
    for every behavior identified in the Phase A feature inventory.
Findings:
  - Decision #4 (retain the CLI) concretely requires one additional
    project (Atmos.Core) beyond CLAUDE.md's original two-project sketch —
    justified by genuine code-sharing need, not speculative layering.
  - The JSON API surface is best implemented as Minimal API endpoints,
    not Razor Page handlers or MVC controllers — no HTML is ever rendered
    for these routes.
  - A History-API navigation model preserves today's reload-free search
    UX while adding real, bookmarkable /weather URLs — an additive
    capability, not requested but low-risk and reversible.
Decisions:
  - See §18 for the full preserved/improved/removed/deferred table.
  - No feature is being removed.
Open questions:
  All five resolved 2026-08-27 (§20 Decisions):
  1. Nominatim User-Agent: AtmosWeather/1.0 (+repo link) — approved.
  2. Same-origin check (not antiforgery tokens) — approved.
  3. No GitHub Actions CI at this time — declined.
  4. Atmos.Core as a third project — approved.
  5. Bookmarkable /weather navigation model — approved.
Risks:
  - Unchanged from Phase A §18 (radar/map-picker fidelity and first-time
    IIS/SQL Server deployment remain the two High-risk items; Stage C+
    exists specifically to de-risk the second one early).
Recommended next phase:
  Phase C — Build Environment. Establish and validate the .NET SDK,
  project scaffolding (Atmos.Core/Atmos.Web/Atmos.Cli/tests as specified
  in §2), SQL Server connectivity, EF Core tooling, and test execution —
  proven with the Stage C+ walking skeleton before Phase D feature work
  begins.
```

Phase B is complete. All open questions resolved 2026-08-27 (§20). Per CLAUDE.md §24, Phase C may begin.
