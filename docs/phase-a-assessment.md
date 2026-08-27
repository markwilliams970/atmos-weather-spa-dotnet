# Phase A — Architectural Assessment

**Subject:** Modernization of Atmos Weather (TypeScript/Node.js/SQLite) → ASP.NET Core 10 / Razor Pages / SQL Server
**Reference implementation inspected:** `../atmos-weather-spa` (commit `063d5cf`, "Add map-based location picker with elevation and nearby-place lookup")
**Status:** Phase 0 (baseline) and Phase A (assessment) complete. No implementation performed. Neither repository was modified.

Throughout this document, findings are labeled:

- **Observed** — behavior actually found in the existing application, with file/line references.
- **Recommended** — proposed design for the .NET application.
- **Open Question** — something that needs Mark's decision before Phase B is considered final.

---

## 1. Executive Summary

**Observed.** Atmos is a single-process Node.js/TypeScript application, deliberately built as a one-file learning project. `weather-server.ts` (2,390 lines) contains the SQLite schema, session handling, a WMO weather-code table, TypeScript interfaces that double as the client/server contract, unit-conversion helpers, seven external-API fetch functions, an entire SPA (CSS + HTML + browser JavaScript) as one template literal, seven HTTP request handlers, and the `http.createServer` bootstrap — all in top-to-bottom sequence with no modules, no framework, no build step, and no automated tests. A companion `weather.ts` (207 lines) is a fully independent CLI that duplicates the WMO table and unit helpers by design.

**What works well.** The application is unusually well-crafted for its stated scope:
- Real TypeScript discipline on the server (`strict: true`, explicit interfaces, no `any`).
- A visually elaborate, fully hand-built frontend — custom SVG gauges/charts, a from-scratch Web-Mercator slippy map (used twice: read-only radar, interactive picker), 12-theme animated sky system — with zero UI/mapping/charting dependencies.
- Deliberate, documented trade-offs (e.g., "fail fast" on cosmetic lookups, no shared module between CLI and server) rather than accidental debt.
- An existing engineering-context file (`Claude.md`) that is unusually candid and accurate about the app's own weaknesses — this assessment independently verified its claims against source and found them accurate throughout.

**Most significant architectural limitations.**
1. Zero automated tests; the `npm test` script is a placeholder.
2. Almost no outbound HTTP timeouts, retries, or cancellation — only the two Overpass/Nominatim calls inside `fetchNearbyPlace` carry an `AbortSignal.timeout`.
3. The client/server JSON contract is entirely implicit — TypeScript interfaces on the server, positional field access in untyped ES5 browser JS, no runtime validation on either side.
4. One state-changing endpoint (`/api/save-units`) uses GET; `/api/weather` also has a GET-triggered side effect (saving to Recent).
5. Map-picked elevation is not persisted, so re-selecting a map point from Recent silently loses its elevation correction — a known, documented gap that CLAUDE.md explicitly requires fixing.
6. No structured logging/observability; several `catch` blocks are silent by design.
7. Hardcoded configuration (port, DB filename, all API base URLs).

**Overall migration assessment: low-to-medium difficulty, low-to-medium risk.** The backend domain logic (unit conversions, WMO mapping, forecast shaping) is small, pure, and directly portable. The genuinely hard parts — the radar tile renderer and the interactive map picker — are already working, self-contained, dependency-free JavaScript; porting them to the .NET app means *relocating and modularizing* that JavaScript, not rewriting it in C#, which keeps the highest-complexity code at the lowest rewrite risk. The real unknowns are environmental (first deployment of this exact stack to IIS/SQL Server on Windows Server 2025) rather than logical. See §18 for a full risk ranking.

---

## 2. Current Architecture

**Observed — actual architecture (not intended):**

```text
Browser (single HTML document, inline <style> + inline <script>)
   │
   │  every request (incl. "/") hits one process
   ▼
Node.js http.createServer  (weather-server.ts, port 3000 hardcoded)
   │  manual if/else-if routing on url.pathname — no router, no middleware
   │
   ├── GET /                → returns the entire HTML template literal verbatim
   │                           (sets session cookie if absent)
   │
   ├── GET /api/weather     ─┐
   ├── GET /api/geocode      │
   ├── GET /api/recent       │  each handler: inline validation →
   ├── GET /api/save-units   │  res.writeHead/res.end JSON, no shared
   ├── GET /api/air-quality  │  response helper
   ├── GET /api/elevation    │
   └── GET /api/nearby-place ┘
             │
             ├──────────────► better-sqlite3 (sync, file-backed)
             │                 weather-searches.db → recent_searches table
             │
             └──────────────► External APIs (all keyless, all unauthenticated fetch())
                                Zippopotam · Open-Meteo (forecast/geocode/AQ/elevation)
                                Overpass · Nominatim · RainViewer · CartoDB
```

Session handling: a hand-rolled cookie parser (`parseCookies`, `weather-server.ts:39`) and a 32-hex-char `sid` cookie issued via `crypto.randomBytes(16)` (`weather-server.ts:48-54`), `HttpOnly`, `SameSite=Lax`, 1-year `Max-Age`. No session store beyond the `session_id` column on `recent_searches` — there is no separate sessions table; the cookie value is simply a foreign key used to scope queries.

Rendering: the entire SPA — CSS custom properties, all markup, and the browser `<script>` — is one JS template literal (`const HTML = ...`, lines 389–2256) returned as `text/html` for every `/` request. There is no `/public` directory and no separately cacheable static asset — the browser re-downloads the full ~113KB document (HTML+CSS+JS combined) on every navigation, and cannot cache the JS/CSS independently of the markup the way separate static files would allow.

Client-side: a single global `state` object (`data`, `units`, `lat`, `lon`, `debounce`, `suggIdx`, `elevation`, `elevationWarning`), no virtual DOM, no framework — every render function does direct `innerHTML`/`textContent` writes through a `$(id)` = `getElementById` helper. `populate(d)` is the single fan-out point called once per successful `/api/weather` response; it triggers gauge/chart drawing, `applyUnits()`, `applyTheme()`, `renderRadarMap()`, and a second independent async fetch (`fetchAndRenderAQ`) for air quality.

---

## 3. Feature Inventory

| Feature | Current implementation | Importance | Migration complexity | Recommendation |
|---|---|---|---|---|
| ZIP search | `handleWeather` → `lookupZip` (Zippopotam) | Core | Low | Preserve as-is |
| City autocomplete | `/api/geocode` proxies Open-Meteo geocoding verbatim, debounced 280ms client-side | Core | Low | Preserve; wrap proxy response in an explicit DTO (see §6) |
| Map picker | Full-screen overlay, hand-built pan/zoom/click slippy map, Web Mercator math (`xyFromLatLon`/`lonLatFromXY`) | Core / important — explicitly called out in CLAUDE.md as must-not-lose | High (interaction fidelity) | Preserve; port JS logic near-verbatim into a module, keep as an overlay (see §11 open question on page structure) |
| Elevation-corrected forecast | `/api/elevation` → Open-Meteo Elevation API; fed back into `/v1/forecast`'s `elevation` param | Important, distinguishing feature | Low (server logic) / Medium (persistence gap) | Preserve, and fix the known non-persistence gap (§5) |
| Nearby-place labeling | `fetchNearbyPlace`: one Overpass query (capped 150 results, no server-side sort) → Nominatim fallback; deliberately fail-fast, no retries | Nice-to-have / cosmetic label enhancement | Low | Preserve behavior including the deliberate "fail fast, no retry" trade-off — do not silently upgrade to a more thorough search |
| Current conditions | Hand-built SVG gauges (`drawHumidity`, `drawUV`, `drawWind`), sun arc | Core | Low (pure rendering, data already shaped) | Preserve |
| 24-hour forecast | SVG temp/precip chart (`drawTempChart`, Catmull-Rom smoothing) + scrollable hour cards | Core | Low-Medium (chart math is intricate but self-contained) | Preserve |
| 7-day forecast | High/low range bars (`renderDaily`) | Core | Low | Preserve |
| Air quality | Independent `/api/air-quality` fetch fired after main payload renders; own SVG gradient bar | Core | Low | Preserve pattern (decoupled fetch = good existing resilience pattern) |
| Radar | Slippy map at fixed zoom 7, CartoDB dark tiles + RainViewer overlay (`mix-blend-mode: screen`), built from `host`+`path` (not the deprecated raw timestamp — see the documented RainViewer gotcha in `Claude.md`) | Core / explicitly must-preserve per CLAUDE.md | Medium-High (tile math + prior production incident) | Preserve exactly, including the `host + frame.path` construction — do not regress to `frame.time` |
| Recent searches | SQLite `recent_searches`, scoped by session cookie, max 10, delete-then-insert-then-trim (3 statements) | Core | Low | Preserve UX; simplify persistence via EF Core upsert semantics (§5) |
| Units toggle | Client-side instant re-render (`applyUnits`), fire-and-forget `GET /api/save-units` | Core | Low | Preserve UX; fix HTTP verb (§16 of CLAUDE.md already mandates this) |
| Sky themes / animations | 12 theme keys derived from condition + day/night + temp thresholds; procedurally generated DOM layers (stars/rain/snow/clouds/fog/lightning) | Identity feature, explicitly protected in CLAUDE.md | Medium (large, but mechanical, port) | Preserve; do not "simplify" into fewer themes |
| Responsive layout | CSS Grid sidebar↔bottom-nav/drawer switch at 720px/420px breakpoints | Core | Low | Preserve breakpoints and behavior |
| CLI tool (`weather.ts`) | Fully independent script, duplicates WMO table/helpers by design | Optional | Low | Decision required — see §14 |

---

## 4. External API Inventory

| Service | Purpose | Endpoint(s) | Key params | Response fields actually used | Auth | Failure behavior today | Preserve in .NET? |
|---|---|---|---|---|---|---|---|
| Zippopotam.us | ZIP → city/state/lat/lon | `GET api.zippopotam.us/us/{zip}` | path param only | `places[0]["place name"/"state abbreviation"/latitude/longitude]` | None | Non-2xx → thrown `Error`, surfaced as `{error}` 400 to client; no timeout | Yes |
| Open-Meteo Forecast | Current + hourly + 7-day, optional elevation correction | `GET api.open-meteo.com/v1/forecast` | `latitude,longitude,current,hourly,daily,temperature_unit=celsius,wind_speed_unit=kmh,precipitation_unit=mm,timezone=auto,forecast_days=7`, optional `elevation` | current.*, hourly.time/temperature_2m/precipitation_probability/weather_code/is_day, daily.* | None | Same as above; **no timeout** | Yes |
| Open-Meteo Geocoding | City autocomplete | `GET geocoding-api.open-meteo.com/v1/search` | `name,count,language=en,format=json` | Entire response is passed through to the browser unmodified (`res.end(JSON.stringify(await r.json()))`, `weather-server.ts:2296-2306`) | None | try/catch → always 200 with `{results:[]}` on failure (deliberately non-fatal for autocomplete) | Yes — **but wrap the pass-through in an explicit DTO** (see §6 hidden coupling) |
| Open-Meteo Air Quality | US AQI + PM2.5/PM10/O₃/NO₂ | `GET air-quality-api.open-meteo.com/v1/air-quality` | `latitude,longitude,current=us_aqi,pm10,pm2_5,ozone,nitrogen_dioxide,timezone=auto` | `current.*` | None | Thrown error → 400 `{error}`; no timeout | Yes |
| Open-Meteo Elevation | Ground elevation for map-picked point | `GET api.open-meteo.com/v1/elevation` | `latitude,longitude` | `elevation[0]` | None | Thrown error → 400 `{error}`; **no timeout** (only the two `fetchNearbyPlace` calls have one) | Yes |
| Overpass API | Nearest named peak/volcano/bay/cape/strait/glacier/river/stream/settlement within 50km | `POST overpass-api.de/api/interpreter` | Raw Overpass QL string, `out center 150` (capped, unsorted server-side) | `elements[].tags.name*`, `.lat/.lon` or `.center` | None (User-Agent not set for this call) | `try/catch{}` around the whole call; 6s `AbortSignal.timeout`; empty/failure → fall through to Nominatim | Yes, including the documented "no retry, no radius widening" trade-off |
| Nominatim | Fallback nearest-town reverse geocode | `GET nominatim.openstreetmap.org/reverse` | `format=jsonv2,lat,lon,zoom=10,accept-language=en` | `address.city/town/village/hamlet/county` | Requires descriptive `User-Agent` per Nominatim policy (currently `Atmos-Weather-Demo/1.0 (learning project)`) | `try/catch{}`, 5s timeout; failure → `fetchNearbyPlace` returns `null` | Yes — **the User-Agent string must be updated** to identify the .NET app/deployment, not the old demo name |
| RainViewer | Latest radar frame metadata + tiles | `GET api.rainviewer.com/public/weather-maps.json`, then `{host}{path}/256/{z}/{x}/{y}/4/0_0.png` | none | `radar.past[last].time`, `.path`; top-level `host` | None | `try/catch{}` client-side (this call is made **from the browser**, not the server — see note below); failure → basemap renders with no radar overlay, no visible error | Yes — **must preserve the `host + path` construction**; a prior version of this app used the raw `time` timestamp directly and radar silently 410'd — documented incident in `Claude.md:188-197` |
| CartoDB | Dark basemap tiles | `GET {a-d}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png` | subdomain round-robin `a-d` | tile images | None | Browser `<img>` load failure is silent (no `onerror` handling observed) | Yes |

**Hidden coupling identified (Observed):**
1. `/api/geocode` is a *verbatim pass-through* of Open-Meteo's geocoding JSON shape (`weather-server.ts:2296-2306`). The browser's `fmtCityLabel(r)` reads `r.name`, `r.admin1`, `r.country_code`, `r.latitude`, `r.longitude` directly (`weather-server.ts:1310-1315`) — i.e., the UI is coupled to Open-Meteo's exact field names with zero server-side translation. This is the one place in the API surface where "third-party JSON leaks throughout the application," which CLAUDE.md §5 explicitly says to avoid. It must be translated into an explicit `GeocodeResult` app model in the .NET port.
2. **The radar frame lookup (`api.rainviewer.com/public/weather-maps.json`) is called directly from the browser**, not proxied through the server (`weather-server.ts:1940`, inside `renderRadarMap`). This is the one external API the current app does not isolate behind a server-side client at all. CLAUDE.md §5 says "External API behavior must be isolated behind application services/clients" — this is a genuine architectural decision point for Phase B (see Open Question below), not a bug to silently fix without discussion, since proxying it changes latency/caching characteristics of the radar card.
3. The AQI category/color thresholds exist in two independently-maintained places: server (`fetchAirQuality`, `weather-server.ts:286-291`) and browser (`aqCat()`, `weather-server.ts:2180-2187`) — and the browser's `fetchAndRenderAQ` **uses its own client-computed `aqCat(d.usAqi)`** rather than the `category`/`color` fields the server already returns in the payload (`weather-server.ts:2218`). This is live duplicated logic, not dead code — flagged for consolidation in §7.

**Open Question:** Should the RainViewer frame-metadata lookup move server-side (behind an `IRadarService`, consistent with every other external call) in the .NET port, or should it remain a direct browser→RainViewer call for latency reasons? Recommendation is to proxy it server-side for consistency with CLAUDE.md §5 and §12's service-boundary model, but this changes one external round-trip's latency profile and should be an explicit decision, not an incidental one.

---

## 5. Data Model Assessment

**Observed — current schema** (`weather-server.ts:13-23`):

```sql
CREATE TABLE IF NOT EXISTS recent_searches (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  label       TEXT    NOT NULL,
  lat         REAL    NOT NULL,
  lon         REAL    NOT NULL,
  searched_at TEXT    NOT NULL DEFAULT (datetime('now'))
)
-- then, best-effort, wrapped in try/catch (better-sqlite3 throws if column exists):
ALTER TABLE recent_searches ADD COLUMN units      TEXT NOT NULL DEFAULT 'imperial'
ALTER TABLE recent_searches ADD COLUMN session_id TEXT NOT NULL DEFAULT ''
```

No explicit indexes exist beyond the implicit rowid primary key. `getRecentSearches` filters `WHERE session_id = ? ORDER BY searched_at DESC LIMIT 10` with a full scan — invisible at this data volume, but worth an index from day one in SQL Server. `saveSearch` (`weather-server.ts:27-31`) is three sequential prepared statements per call: delete-by-`(session_id, label)`, insert, then delete-outside-top-10 — functionally an upsert-and-trim, not atomic (no transaction wrapper observed), acceptable at SQLite's single-writer scale but worth doing as a proper EF Core transaction or a single set-based operation in SQL Server.

`searched_at` uses SQLite's `datetime('now')`, which returns UTC — so the existing timestamp semantics are **already UTC**, not a change CLAUDE.md's "use UTC timestamps" directive needs to correct, just preserve.

**What should be preserved:**
- The core shape: one row per (session, label), most-recent-10-per-session retention.
- UTC timestamp semantics (already true today).
- Units stored per saved search, not per session.

**What should change moving to SQL Server / EF Core:**
- Replace the delete-then-insert-then-trim pattern with a single upsert (EF Core `Update`-or-`Add` on a natural key, or a raw `MERGE`), executed in a transaction.
- Add the elevation column that's currently missing (below).
- Add explicit indexing.
- Consider a real uniqueness constraint on `(SessionId, Label)` rather than relying on delete-before-insert to fake it.

**Indexes recommended:**
- `IX_RecentSearch_SessionId_LastAccessedUtc` on `(SessionId, LastAccessedUtc DESC)` — supports both the top-10 read and the trim-outside-top-10 delete.

**Data types requiring a deliberate decision:**
- `Latitude`/`Longitude`: SQLite stores `REAL` (double). Recommend `float` (SQL Server `float`/C# `double`) to match, rather than `decimal`, since these values come from external APIs as doubles and are never used for exact/monetary-style comparis0ns.
- `SessionId`: currently a 32-char lowercase-hex string. Recommend `char(32)`/`nchar(32)` fixed-width in SQL Server (matches the fixed format validated by `^[0-9a-f]{32}$`) rather than a variable-length `nvarchar`.
- `Units`: currently free-text `'imperial'`/`'metric'`. Recommend a small check-constrained column or a C# enum mapped to a narrow string — not a bug to fix, just a natural EF Core improvement.
- `Label`: currently unbounded `TEXT` — **Observed gap:** there is no server-side length cap on the `label` query parameter in `handleWeather`'s lat/lon branch (`weather-server.ts:2277-2281`), meaning an attacker-controlled `label` (fully free text via `?lat=&lon=&label=`) is stored without a maximum length. Recommend a bounded `nvarchar(200)` with validation at the model-binding layer in the .NET port — not present today.

**Should currently-transient data become persisted?** Yes — **map-selected elevation**, per CLAUDE.md §9's explicit instruction and the known limitation documented in both `README.md:196-198` and `Claude.md:413-417`. Today, re-selecting a map-picked point from Recent re-fetches a plain (non-elevation-corrected) forecast because `recent_searches` has no elevation column. Add a nullable `ElevationMeters float` column and thread it through the recent-search click handlers exactly as the live map-picker flow already does.

**Recommended entity (conceptual, no migration yet):**

```text
RecentSearch
  Id                int/bigint, identity, PK
  SessionId         char(32), indexed
  Label             nvarchar(200)
  Latitude          float
  Longitude         float
  ElevationMeters   float, nullable          -- NEW: fixes the known gap
  Units             nvarchar(10)             -- 'imperial' | 'metric'
  LocationType      nvarchar(10), nullable   -- 'zip' | 'city' | 'map' (optional — see Open Question)
  CreatedUtc        datetime2(3)
  LastAccessedUtc   datetime2(3), indexed with SessionId
```

**Open Question:** CLAUDE.md's example schema (§9) includes `LocationType`, but no current UI behavior reads or needs it — the app treats every recent search identically regardless of how it was found. Include it now (cheap, forward-compatible) or defer until a feature actually needs it? Recommend including it now since it costs nothing and the column is already anticipated in the governing doc, but confirm with Mark before committing to the migration.

---

## 6. Client/Server Contract

**Observed — every meaningful browser/server interaction, with current (implicit) shapes:**

| Endpoint | Request | Response shape (today) | Notes |
|---|---|---|---|
| `GET /` | — | Full HTML document | Also sets `sid` cookie if absent |
| `GET /api/weather` | `zip=` **or** `lat=&lon=&label=`, `units=`, optional `elevation=` | `WeatherPayload` (server `interface`, `weather-server.ts:109-128`) — location, zip, lat/lon, temp/feelsLike (F+C), humidity, wind (mph/kmh+dir/deg), precip (in/mm), uvIndex, condition+emoji, sunrise/sunset (+ minute-of-day), isDay, todayHigh/Low (F+C), `hourly: HourlySlot[]`, `daily: DailyRow[]`, optional `elevationM` | Side effect: upserts into Recent |
| `GET /api/geocode` | `q=`, `count=` (default 5) | **Raw Open-Meteo geocoding response**, no server-side model | Hidden coupling — see §4 |
| `GET /api/recent` | — (session cookie) | `RecentSearch[]` — `{id,label,lat,lon,units}` | |
| `GET /api/save-units` | `label=`, `units=` | `{ok:true}` | GET-triggered mutation — CLAUDE.md already flags this for correction |
| `GET /api/air-quality` | `lat=`, `lon=` | `AirQualityData` — `{usAqi,pm25,pm10,ozone,no2,category,color}` | Client fetches this *and separately recomputes* category/color client-side (see §7) |
| `GET /api/elevation` | `lat=`, `lon=` | `{elevation: number}` | |
| `GET /api/nearby-place` | `lat=`, `lon=` | `{name: string \| null}` | |

**Implicit contracts identified (Observed):**
- All shapes above are enforced only by convention — TypeScript `interface`s on the server, positional/by-name field access in untyped ES5 browser JS, zero runtime validation on either side. A server-side field rename today would silently break the frontend with no compile-time signal.
- `/api/geocode`'s pass-through shape (see §4).
- Error responses are all `{error: string}` with a `4xx` status, but this shape is never formally declared as a type — it's just what each handler happens to write.

**Recommended for .NET:** explicit C# request/response models for every endpoint above (or every Razor Page handler, if some of these become page handlers rather than public JSON endpoints per CLAUDE.md §16), including a single shared `ApiError { string Message }`-style shape for failures. `/api/geocode`'s response should be translated into an app-owned `GeocodeResult` model rather than passed through.

---

## 7. Domain Logic

**Observed — logic embedded outside where it conceptually belongs:**

1. **Forecast shaping is the single most complex piece of logic in the file** (by the original author's own admission in `Claude.md:87-91`), living inside `fetchWeather` (`weather-server.ts:166-271`): locating "now" in the hourly array by `YYYY-MM-DDTHH` prefix match, slicing 24 hours forward, and building `Today`/`Tmrw`/weekday-name daily labels. This is genuine domain logic (not just DTO mapping) and deserves to be its own testable unit in the .NET port (e.g., a `WeatherForecastMapper` or similar), independent of the HTTP client that fetches the raw Open-Meteo response.
2. **AQI categorization exists twice and is actually used twice**, not just duplicated-and-dormant: server-side in `fetchAirQuality` (`weather-server.ts:284-291`) and client-side in `aqCat()` (`weather-server.ts:2180-2187`), and `fetchAndRenderAQ` calls the *client* version even though the server already computed and returned `category`/`color` in the same payload (`weather-server.ts:2218` vs. the unused `d.category`/`d.color` fields). **Recommendation:** consolidate to one source of truth — the server computes category/color once; the client's JS should render the server-provided fields rather than recomputing them. This is a genuine simplification opportunity the .NET port should take, not a behavior change (the thresholds are identical in both copies today).
3. **Unit conversion and the WMO code table are intentionally duplicated** between `weather.ts` (CLI) and `weather-server.ts` (web) — per `Claude.md:334-336`, this is a *deliberate* isolation choice for the standalone CLI's independence, not an oversight. If the CLI is retained in the .NET port (§14), this duplication should be resolved by sharing the WMO table and conversion helpers through a common library — which is easy to do in .NET or now that "independent single script" is no longer the guiding constraint) and is one of the explicit "Improvements Explicitly Encouraged" in CLAUDE.md §28 ("duplicated domain logic").
4. **Elevation unit conversion is duplicated client-side**: the server never returns elevation in feet; `applyUnits()` computes `elevationM * 3.28084` client-side (`weather-server.ts:1743`), matching the pattern used for every other imperial/metric pair in the file. This is consistent with the existing pattern (all client-visible unit toggling is done client-side against server-provided base values) — **preserve this pattern**, don't move it server-side, since it lets the °F/°C toggle re-render instantly with no refetch (an explicitly documented design goal, `Claude.md:214-220`).
5. **HTML generation entangles presentation with the server module itself** — the entire SPA is a string built in the same file as the DB/session/API logic, which is the direct cause of "no separately cacheable static assets" noted in §2. Splitting `wwwroot/` static files out from server logic is exactly what Razor Pages + `wwwroot` gives for free.

**Recommended consolidation for .NET:** one domain/service layer owns WMO mapping, unit conversion, AQI categorization, and forecast shaping; Razor Pages/JSON endpoints and (if retained) the CLI both consume it; the browser receives fully-shaped values and only does *display-unit* toggling (°F/°C, m/ft) against values the server already computed — matching today's actual client/server split, just with the accidental duplication (item 2 above) removed.

---

## 8. Security Assessment

**Observed, current state:**

| Concern | Current state |
|---|---|
| Input validation | ZIP: `/^\d{5}$/`. Session cookie: `/^[0-9a-f]{32}$/` before trusting an incoming cookie (`weather-server.ts:50`). `lat`/`lon`: `NaN` checks before use in every coordinate-accepting handler. **Gap:** `label` (free text via `?lat=&lon=&label=`) has no length or content validation at all — stored as-is. |
| Session cookie | `HttpOnly`, `SameSite=Lax`, 1-year `Max-Age`, 128 bits of entropy (`crypto.randomBytes(16)`). **No `Secure` attribute** — acceptable for local `http://localhost` dev, must be added for the IIS/HTTPS deployment target. |
| State-changing GET | `/api/save-units` (pure mutation) and `/api/weather` (side-effecting write to Recent) are both GET. No CSRF protection exists anywhere — combined with GET-triggered mutation, a third-party page could forge a same-session request via a plain `<img>`/navigation to pollute a victim's Recent list or overwrite their saved unit preference. Low real-world impact (no PII, no financial/account action) but a real CSRF gap by strict definition. |
| HTML encoding | `escHtml()` (`weather-server.ts:1867`) is applied to the one place a user-influenced value (`label`) is written via `innerHTML` (the sidebar/drawer recent-item list, `weather-server.ts:1881`). It escapes `& < > "` but not `'` — safe in this specific usage since the attribute delimiter is `"`, but it is a hand-rolled escaper, not a systematic policy. Every other server-sourced string observed (`location`, `condition`, coordinates, elevation, AQ values) is written via `textContent`, which is safe. No systematic audit exists; this was manually verified place-by-place during this assessment. |
| External requests | All unauthenticated, no secrets involved. Nominatim's required descriptive `User-Agent` is set (`Atmos-Weather-Demo/1.0`) — **must be updated** for the new deployment's identity per Nominatim's usage policy. |
| Secrets/config | None exist today (all APIs keyless); port/DB filename/API base URLs are hardcoded literals, not secrets, but still should move to configuration per CLAUDE.md §13 for future-proofing (a future API requiring a key would otherwise be hardcoded too). |
| SQL access | 100% parameterized via `better-sqlite3` prepared statements (`db.prepare(...).run(...)`) — no injection risk found anywhere in the file. |
| HTTPS | None in dev (plain `http://localhost:3000`); required for the IIS deployment target per CLAUDE.md §17. |
| CSRF/anti-forgery | None exists; not applicable in the current architecture since there are no traditional form posts, but becomes directly relevant the moment `/api/save-units` (or equivalent) becomes a real POST/PUT with anti-forgery expectations, per CLAUDE.md §17. |

**Not attempting fixes at this stage** (per instructions) — these are flagged for Phase B/D design and CLAUDE.md's existing §17 requirements already cover most of them (secure cookies, anti-forgery for state-changing requests, output encoding, parameterized access).

---

## 9. Reliability Assessment

**Observed:**
- **Timeouts:** Only `fetchNearbyPlace`'s two calls (Overpass 6s, Nominatim 5s) carry `AbortSignal.timeout`. `lookupZip`, `fetchWeather`, `fetchAirQuality`, `fetchElevation`, and the browser-side RainViewer frame lookup have **none** — a slow upstream stalls the request indefinitely from the caller's perspective.
- **Retries:** None anywhere. This is consistent, not accidental — even the two calls with timeouts explicitly don't retry (documented as a deliberate "fail fast" choice for a cosmetic feature in `Claude.md:107-108, 418-427`).
- **Cancellation:** No `AbortController`/cancellation token propagation from the HTTP request lifecycle into the outbound fetches — if a client disconnects mid-request, the server-side fetch keeps running to completion.
- **Partial-failure handling — the good news:** two real graceful-degradation patterns already exist and should be explicitly preserved, not just replicated by accident:
  1. Air quality is fetched **after** `populate()` renders the core forecast, as an independent async call (`weather-server.ts:1776`, `fetchAndRenderAQ`) — a slow/failed AQ call never blocks or breaks the forecast display; it just shows "Unavailable" in that card.
  2. Elevation/nearby-place failures during map-pick surface as a non-blocking `state.elevationWarning` shown via `setStatus()` **after** the forecast is already populated (`weather-server.ts:2160-2167`, `Claude.md:278-280`) — the forecast is never held hostage by these optional enhancements.
- **Silent failures:** `loadRecent()` (`weather-server.ts:1915-1920`), the geocode-suggestion fetch (`weather-server.ts:1345-1352`), the RainViewer frame lookup (`weather-server.ts:1939-1948`), and both branches of `fetchNearbyPlace` all swallow errors into empty `catch{}` blocks with nothing logged anywhere. This is fine for *user-facing* degradation (matches the graceful-degradation goal) but leaves **zero trace for debugging** a production failure — there is no log line anywhere these fire.
- **Error message exposure:** `handleWeather`/`handleAirQuality`/`handleElevation` all catch and forward `(err as Error).message` verbatim to the client as `{error: message}` with a 400 (`weather-server.ts:2290-2292` etc.) — today these are just plain thrown-`Error` messages (e.g., `"ZIP code "80002" not found."`), not stack traces or raw exception internals, so the practical exposure risk is low, but CLAUDE.md §15 explicitly asks the .NET version not to reproduce "expose raw third-party exception messages" as a pattern, which this borders on.
- **Logging:** a single `console.log` on server startup (`weather-server.ts:2389`) is the *only* log output that exists anywhere in the application.

**Highest-value reliability improvements for the .NET port** (per CLAUDE.md §29, already directed): add explicit timeouts to every outbound call (`HttpClient` `Timeout` or per-request `CancellationToken`), keep the existing "fail fast, no retry" policy specifically for the nearby-place/elevation cosmetic lookups (this is a preserved *design decision*, not a gap to "fix" with retries), add structured logging around every currently-silent catch, and translate raw exception messages into the UI-facing categories CLAUDE.md §15 already specifies (invalid input vs. unavailable data vs. unavailable enhancement vs. unexpected error).

---

## 10. Testing Assessment

**Observed:** zero automated tests exist. `package.json`'s `test` script is the npm-generated placeholder (`echo "Error: no test specified" && exit 1`). The only automated check possible today is `npx tsc --noEmit` (type-checking, not behavior verification). Every behavior described in this document was verified by reading source, not by running an existing test suite, because none exists.

**What is realistically unit-testable once ported** (pure, deterministic, no I/O):
- WMO code lookup + unknown-code fallback (`wmo()`)
- Unit conversions: `cToF`, `kmhToMph`, `mmToIn`, elevation m→ft
- `degToCompass`
- AQI categorization thresholds (once consolidated per §7)
- `haversineKm`
- `latinName` (the Unicode-script extraction regex — has real edge-case surface: mixed-script names, empty results, names under the 2-character minimum)
- Forecast transformation: the hourly-window-selection + daily-labeling logic inside `fetchWeather` — the single most complex logic in the app per its own author, and the highest-value test target given fixture Open-Meteo JSON
- Recent-search retention logic: trim-to-10-per-session, once expressed as its own testable unit rather than three inline SQL statements

**What is integration-testable:**
- Application/EF Core startup and migrations
- Page/endpoint routing
- Invalid-input handling (bad ZIP, non-numeric coordinates, missing params) — mirrors the validation branches already present in every handler
- Session cookie creation and reuse
- Recent-search persistence end-to-end (save → retrieve → trim)
- Unit-preference persistence end-to-end

**What is not practically testable today and should be, post-port, via mocked fixtures rather than live network:** every external API call. The current app has **no mocking seam at all** — every fetch hits the real internet, every time, with no test suite exercising any of it. The .NET port should introduce an `HttpClient`-based abstraction specifically so fixture-based tests (per CLAUDE.md §18) become possible, which is a genuine capability gain over the current state, not a preservation of existing behavior.

**Lowest priority (per CLAUDE.md §18's own instruction not to over-invest here):** the theme engine, procedural star/rain/snow/cloud/fog generation, and CSS animation timing — cosmetic, high cost to test meaningfully, low value.

---

## 11. UI/UX Assessment

**Observed — the current experience is not a true multi-view SPA; it is one continuously-visible view.** The search bar, hero card, and tab panel all live in the same DOM at all times — there is no distinct "landing/search" state versus "results" state beyond the hero panel's empty placeholder (`—`) values before a first search, and `#wx-panels.show` toggling visibility. The map picker is a full-screen `overlay` (`weather-server.ts:1256-1274`), not a navigation — opening it does not change the URL or leave the current view.

**What should be preserved as-is:**
- The Current/24-Hour/7-Day tab structure as tabs within one weather view — CLAUDE.md §8 already mandates this explicitly, and this assessment agrees: these three views share one dataset (`state.data`) and are cheap, instant client-side switches (`switchTab`); splitting them into separate page navigations would add round-trips for zero benefit and contradicts the existing, working UX.
- The sidebar (desktop) / bottom-nav + slide-up drawer (mobile) pattern for Recent — this is a well-executed, already-responsive pattern with no reason to change.
- All dynamic/client-side behavior: sky themes and animations, radar tile rendering, map-picker interaction, chart/gauge drawing, tab switching, autocomplete presentation — CLAUDE.md §34 already assigns these to the browser, and nothing in the current implementation argues otherwise.

**Where the current single-view design maps awkwardly onto CLAUDE.md's proposed multi-page structure:**

1. **Search vs. results are not actually separate screens today.** CLAUDE.md's proposed `/` (Home/Search) and `/weather` (results) split is reasonable for Razor Pages routing/bookmarking purposes, but the current UX lets a user search again *without ever leaving the results view* — the search bar never disappears. If `/` and `/weather` become genuinely separate pages, the search input should live in a **shared layout partial** present on both, not just on `/`, or the port will regress a currently-working "search again from anywhere" flow into a "go back home to search again" flow.
   **Open Question:** Confirm the search bar should be part of the shared `_Layout`, persistent across `/` and `/weather`, rather than page-local to `/` only.

2. **The map picker is an overlay, not a page, and CLAUDE.md proposes `/map` as a page.** The current picker is opened from a link inside the search-suggestions dropdown (`weather-server.ts:1332-1340`, `openMapPicker()`) and closes back into whatever the user was doing — no navigation occurs. Converting this into a true page navigation (leaving `/` or `/weather` to go to `/map`, then navigating back) would be a regression in interaction cost for what is currently a two-click, no-navigation flow.
   **Open Question:** Should `/map` exist purely as a deep-linkable/bookmarkable full-page entry point (for someone who lands there directly), while the actual day-to-day interaction remains a JS-driven overlay launched from wherever the user currently is (as today)? This assessment recommends **yes** — keep the overlay behavior for the primary flow, and treat `/map` as an optional secondary entry point — but this is a deviation from reading CLAUDE.md's page list as "one interaction path per route," so it needs Mark's explicit sign-off before Phase B locks it in.

3. **`/about` is new** — no equivalent content exists in the current app (`Claude.md`/`README.md` exist as repo docs but not as in-app content). Low risk, needs new copy (data sources, attribution — the app already surfaces attribution text: `"Data · Open-Meteo · Zippopotam.us · RainViewer · No API key required"`, `weather-server.ts:1227`, and `"RainViewer · © OpenStreetMap · © CARTO"`, `weather-server.ts:1206`).

**Server-rendered vs. client-dynamic split for the new app:** the initial page shell (layout, empty hero/tab structure, `/about` content) belongs naturally in Razor; everything the current app already does client-side (search-as-you-type, tab switching, all rendering after the initial JSON fetch, radar, map picker, themes) should remain client-side JS exactly as CLAUDE.md §34 specifies — nothing in this assessment argues for moving any of that logic server-side.

---

## 12. Recommended Target Architecture

**Assessment of CLAUDE.md's proposed direction (ASP.NET Core 10 / Razor Pages / EF Core / SQL Server / modular JS / IIS): agree, no changes recommended.**

Reasoning specific to this application (not generic best practice):
- The app has no complex client-side state graph, no client-side routing beyond three tabs and a modal, and no need for component reactivity beyond direct DOM writes it already does by hand. Razor Pages' server-rendered shell + progressively-enhanced JS islands is a good structural match for what's already there — this is not a SPA with deep client state that Razor Pages would strain to support.
- The hardest, highest-risk code in the entire application (radar tile math, map-picker pointer-event handling, SVG chart/gauge generation, the theme engine) is **pure client JavaScript today, with zero server dependency**, and stays pure client JavaScript under Razor Pages exactly as it is under the current Node server. Choosing Blazor Server/WebAssembly would force a rewrite of all of that hand-tuned, already-working code into C# for no functional gain — directly contradicted by CLAUDE.md §7's explicit instruction not to reinterpret "port to .NET" as "rewrite all browser JavaScript in C#."
- EF Core + SQL Server is heavier than the current single-table SQLite setup, but this is an explicit deployment-target requirement (Windows Server/IIS/SQL Server), not a technology chosen for its own sake — and the actual data model (§5) is simple enough that EF Core's overhead is negligible in practice.
- Modular JS (`wwwroot/js/{app,search,weather,charts,radar,map-picker,themes}.js`) directly solves the one real architectural weakness of the current frontend (§2's "no separately cacheable static assets," §7's HTML/CSS/JS entanglement) without requiring a framework.

No architectural changes to CLAUDE.md's direction are recommended coming out of this assessment.

---

## 13. Recommended Page Structure

Building on §11's UX findings, the following refines — but does not fundamentally alter — CLAUDE.md's starting hypothesis:

```text
/          Home / Search
              — search input + autocomplete (shared layout, see below)
              — if a ZIP query string or an active session selection exists,
                the search can resolve straight to results without a second click
                (mirrors today's ?zip= URL behavior, weather-server.ts:2252-2253)

/weather   Current | 24-Hour | 7-Day tabs (unchanged from current behavior)
              — hero card + tab nav, exactly as today
              — search input present here too (see Open Question, §11.1)

/map       Optional direct-entry page hosting the picker as a full view
              — primary interaction remains a JS overlay launched from
                wherever the user currently is (search dropdown), preserving
                today's no-navigation flow (see Open Question, §11.2)

/about     Application information and data sources (new content)
```

The recent-search sidebar/drawer remains part of the shared layout (`_Layout.cshtml`), present on `/` and `/weather` alike, matching current behavior where Recent is always reachable regardless of what's on screen. Do not split Current/24-Hour/7-Day into separate routes — confirmed against actual usage pattern (single shared dataset, instant client-side switch), not just following CLAUDE.md's instruction not to.

---

## 14. Recommended .NET Solution Structure

**Agree with CLAUDE.md's proposed structure**, with one conditional addition:

```text
Atmos.sln
src/
    Atmos.Web/
        Pages/
        Services/
        Models/
        Data/
        Infrastructure/
        wwwroot/
        Program.cs
tests/
    Atmos.Tests/
```

**Explicitly do NOT introduce**, for the reasons below (this small application does not need any of them):
- A separate `Atmos.Domain` or `Atmos.Infrastructure` class library — one small web project with internal folder separation (`Services/`, `Models/`, `Data/`) gives the same organizational clarity without cross-project ceremony, for an app with one entity and ~7 external services.
- A repository layer wrapping EF Core — `DbContext` + a handful of `IRecentSearchService` methods is already the right level of abstraction; a repository here would wrap an abstraction (EF Core) that is already an abstraction, adding indirection with no testability gain EF Core's own `DbContext` mocking/in-memory providers don't already give.
- CQRS or MediatR — there is exactly one write path (`RecentSearch` upsert) and a handful of read paths; introducing a mediator pattern for this is the textbook definition of the "architecture astronautics" CLAUDE.md §26 warns against.
- Any additional class library **unless** the CLI is retained (see below), in which case a small shared library is justified — not for "clean architecture" reasons, but because two executables (`Atmos.Web`, `Atmos.Cli`) need to call the same `WeatherService`/domain models without duplicating them, which is the one place code-sharing actually matters here.

**CLI retention decision (CLAUDE.md §35 requires an explicit call):**

| Factor | Assessment |
|---|---|
| Current size/complexity | 207 lines, fully self-contained, no server/DB/session dependency |
| Value | Nice-to-have terminal demo; not referenced by any core requirement in CLAUDE.md §4 |
| Effort to port | Low — a console app calling the same `IWeatherService`/`IGeocodingService` (ZIP lookup) the web app uses, plus a console-formatting layer analogous to `display()`/`box()` |
| Duplication if retained without sharing | Would reintroduce the exact WMO-table/unit-helper duplication CLAUDE.md §28 lists as a weakness to fix |

**Recommendation:** retain the CLI as `Atmos.Cli`, sharing weather/geocoding services and domain models with `Atmos.Web` via a small shared library (or via the `Atmos.Web` project itself if `Atmos.Cli` is willing to reference it — a lighter option worth considering given the "avoid unnecessary project fragmentation" directive). Sequence it late, as CLAUDE.md's own D15 already does. **Open Question:** confirm this is worth doing at all — it is optional-value work per CLAUDE.md §35's own framing ("only if the CLI provides meaningful value").

---

## 15. SQL Server Design (Conceptual)

Already detailed in §5; summarized here per the Phase A outline:

**Entities:** one — `RecentSearch`. No user/account entity (no accounts, no login, matches CLAUDE.md §4/§27's explicit "no account requirement").

**Relationships:** none beyond the implicit grouping by `SessionId` — there is no `Session` table today and none is needed; the session cookie is a bare correlation key, exactly as in the current SQLite design.

**Indexes:** `(SessionId, LastAccessedUtc DESC)` to support both the top-10 read and the trim-to-10 delete in one covering index.

**Constraints:** `SessionId` fixed-length hex (`char(32)`); `Units` narrow/enum-backed; `Label` bounded length (`nvarchar(200)`, a new validation not present today — see §5); consider a real `(SessionId, Label)` uniqueness rather than the current app's delete-before-insert emulation of it.

**Session/recent-search strategy:** unchanged in shape from today — cookie-scoped, no accounts, max 10 per session — implemented via EF Core upsert + a single trim operation instead of three raw statements.

**Timestamp strategy:** `datetime2(3)`, UTC, matching the current app's *already-UTC* semantics (no behavior change, just an explicit type instead of SQLite's implicit text-affinity `TEXT` column).

**Elevation persistence:** new nullable `ElevationMeters float` column, closing the one functional gap CLAUDE.md explicitly calls out (§9: "The original application's known limitation where map-selected elevation is not persisted should be corrected in the .NET version").

No migrations are created at this stage, per the Phase A constraints.

---

## 16. Testing Strategy

Building on §10's assessment:

**Unit tests** (xUnit, no live network, no DB): WMO mapping, all unit conversions, compass conversion, AQI categorization (post-consolidation, §7), haversine distance, `latinName`-equivalent Unicode-script extraction, forecast-shaping logic (hour-window selection, daily labeling) against fixture JSON modeled on real Open-Meteo responses, and recent-search trim-to-10 logic as a pure, isolable unit.

**Integration tests** (`Microsoft.AspNetCore.Mvc.Testing` / `WebApplicationFactory`, against a real or ephemeral SQL Server — e.g., LocalDB for CI, or a Testcontainers SQL Server instance): application startup, EF Core migrations applying cleanly, page/endpoint routing, invalid-input rejection (mirroring every validation branch already present in the current handlers), session cookie issuance/reuse, recent-search persistence end-to-end, unit-preference persistence end-to-end. External API calls in these tests should be mocked via a fake `HttpMessageHandler` fed fixture JSON — a capability the current app has never had, since every current call hits the live internet.

**Browser tests** (Playwright for .NET, deferred until the core app is stable, per CLAUDE.md §18's own sequencing): prioritized exactly per CLAUDE.md's existing order — ZIP search → city autocomplete → forecast rendering → recent-search selection → unit switching → map picker → map-selected forecast → radar rendering. Do not over-invest in testing theme/animation cosmetics (agrees with CLAUDE.md's explicit instruction).

**Recommended stack:** xUnit, `Microsoft.AspNetCore.Mvc.Testing`, Playwright for .NET — no additional assertion library needed beyond what xUnit provides, keeping dependencies minimal per CLAUDE.md §25.

---

## 17. Build and Deployment Strategy

- **.NET SDK/tooling:** .NET 10 SDK, `dotnet` CLI, `dotnet-ef` global/local tool for migrations.
- **Development environment:** a normal developer workstation (compilation, unit/integration tests, EF Core migrations against LocalDB or a local SQL Server Developer-edition instance) — the Windows Server 2025 VM is reserved for SQL Server/IIS integration and deployment testing, per CLAUDE.md §20.
- **SQL Server tooling:** SQL Server Developer edition (or LocalDB for fast local iteration) + SSMS or Azure Data Studio for schema inspection during development.
- **IIS requirements:** ASP.NET Core Hosting Bundle installed on the Windows Server 2025 host; a dedicated application pool (No Managed Code, since ASP.NET Core is self-hosted behind the ASP.NET Core Module); appropriate filesystem permissions for the app pool identity.
- **Publish strategy:** `dotnet publish --configuration Release`, framework-dependent deployment (not self-contained) per CLAUDE.md §21; the `web.config` should be the one generated by `dotnet publish`, not hand-maintained.
- **Configuration strategy:** `appsettings.json` + `appsettings.Development.json` for local dev; environment-specific values (SQL Server connection string, any future API keys) supplied via `appsettings.Production.json` or environment variables on the VM — never hardcoded, even though today's external APIs are keyless (CLAUDE.md §13 explicitly requires this for future-proofing).
- **Deployment process:** IIS site + app pool configuration → apply EF Core migrations (explicit step, documented — CLAUDE.md §22 forbids implicit `CREATE TABLE IF NOT EXISTS`-style runtime schema manipulation) → HTTPS binding → smoke test (matches the Phase E checklist already in CLAUDE.md §23).

---

## 18. Migration Risks

```text
High
```
- **Radar tile renderer + interactive map picker fidelity.** Hand-tuned Web Mercator math and pointer-event drag/zoom/click disambiguation (`pickerState.dragMoved` threshold, `setPointerCapture`, the explicit `.map-picker-zoom` button exclusion in the `pointerdown` handler) are exactly the kind of interaction code that's easy to subtly regress during a port and hard to catch with automated tests — CLAUDE.md itself calls the map picker a must-not-lose feature. Mitigation: port the JS logic as directly as possible rather than "improving" it during the move, and manually verify pan/zoom/drag/click behavior in-browser per CLAUDE.md §19's Definition of Done.
- **First IIS/SQL Server deployment of this exact stack to this exact environment.** Nothing about this is a code-porting risk — it's operational unknowns (app pool identity/permissions, SQL Server auth mode, HTTPS binding, hosting bundle version compatibility with .NET 10) that won't surface until the first real deployment attempt. Mitigation: see §19's recommended early "walking skeleton" deployment rehearsal, rather than deferring all deployment risk to Phase E.

```text
Medium
```
- **Visual theme engine port.** 12 themes × 5 animated layer types is a large but mechanical translation; the risk is in faithfully preserving randomized-but-bounded visual parameters (`rnd()` ranges for star twinkle timing, rain density, etc.) rather than any algorithmic difficulty.
- **SQL Server schema/migration decisions.** Conceptually simple (one table) but genuinely new logic (upsert-and-trim, elevation persistence, indexing) rather than a direct line-for-line port — see §5/§15.
- **Client/server contract formalization.** Turning today's implicit JSON shapes into explicit C# DTOs risks a silent field-name drift breaking the JS if not done carefully and tested end-to-end (§6, §16 integration tests should specifically target this).

```text
Low
```
- **Backend domain logic** (unit conversions, WMO table, forecast transformation) — small, pure, deterministic, directly transliterable, and the highest-value/lowest-effort unit test target (§10, §16).
- **External API integration** — identical HTTP APIs and query parameters; only the client library changes (`fetch` → `HttpClient`).
- **Session/recent-search behavior** — one cookie, one table, straightforward in EF Core.

---

## 19. Recommended Implementation Sequence

CLAUDE.md's D1–D18 sequence (§23, Phase D) is sound and is **endorsed with one structural addition**, not a reordering of the feature work itself: insert an explicit early **deployment rehearsal** milestone, rather than leaving all IIS/SQL Server deployment risk concentrated in Phase E at the very end. Given §18's finding that the deployment environment is the single largest *unknown* (as opposed to the largest *coding* risk), validating it early — with a trivial app — de-risks everything built afterward.

| Stage | Objective | Dependencies | Deliverable | Test requirement | Completion criteria |
|---|---|---|---|---|---|
| **C (existing, Phase C)** | Prove the toolchain | None | Minimal working app locally | Build succeeds | `dotnet run` serves a page locally |
| **C+ (new — recommended addition)** | Deployment rehearsal: deploy the *minimal* Phase C app to IIS on the Windows Server 2025 VM, with one trivial EF Core migration against real SQL Server | Phase C | A "hello world" Razor Pages app, live on the VM, reading/writing one dummy table via EF Core | Manual smoke test (page loads over HTTPS, DB round-trip works) | Confirms hosting-bundle/app-pool/SQL-auth/HTTPS mechanics work *before* any real feature is built on top of them |
| D1–D4 (existing) | Solution skeleton, config/logging, SQL Server + EF Core (real schema), session/recent-search persistence | C+ | Working persistence layer | Unit tests for trim/upsert logic; integration test for session cookie + recent-search round-trip | `RecentSearch` CRUD works end-to-end against real SQL Server |
| D5–D6 (existing) | External API clients, weather domain/application services | D1–D4 | `IWeatherService`, `IGeocodingService`, etc., with mocked-fixture unit tests | Unit tests against fixture JSON (§16) | Forecast-shaping logic passes fixture-based tests without live network |
| D7 (existing) | Basic forecast page | D5–D6 | `/weather` renders real data | Manual verification of Current/24h/7-day tabs | Visual parity with the reference app for a known ZIP |
| D8–D10 (existing) | Search/autocomplete, units, air quality | D7 | Full search flow, unit toggle, AQ card | Integration test for unit persistence; manual verification | Matches reference app behavior including the AQ graceful-degradation pattern (§9) |
| D11–D12 (existing, flagged High risk in §18) | Map picker, radar | D7 | Ported JS modules for both | Manual in-browser verification per Definition of Done (CLAUDE.md §19); no meaningful automated coverage expected here | Pan/zoom/click/drag behavior matches reference app; radar tiles render using `host + path` (not `time`) |
| D13–D14 (existing) | Dynamic weather themes, responsive/mobile refinement | D7–D12 | Modular `themes.js`; verified at both breakpoints (720px/420px) | Manual verification across breakpoints | Visual parity with reference app on desktop and mobile |
| D15 (existing, conditional — §14) | CLI functionality, if retained | D5–D6 (shared services) | `Atmos.Cli` | Reuses D5–D6's unit tests via shared services | Only proceed if Mark confirms the CLI is worth retaining |
| D16–D17 (existing) | Integration tests, browser tests | All feature work | Full automated suite | N/A (this *is* the test work) | Coverage matches §16's stated priorities |
| D18 (existing) | Documentation and cleanup | All | `README.md`, `ARCHITECTURE.md`, `DEPLOYMENT.md` updated | N/A | Docs reflect final state, not aspirational state |

---

# Phase A Recommendation

```text
Overall migration assessment:
  Low-to-medium difficulty, low-to-medium risk. The backend domain logic is
  small and pure; the highest-complexity code (radar, map picker, theme
  engine, SVG charts) is already-working, dependency-free browser JS that
  gets relocated and modularized rather than rewritten. The largest
  unknowns are operational (first IIS/SQL Server deployment to this exact
  environment), not architectural.

Recommended target architecture:
  ASP.NET Core 10 / Razor Pages / EF Core / SQL Server / modular browser
  JS / IIS, exactly as proposed in CLAUDE.md — confirmed appropriate
  against the actual application, no changes recommended.

Recommended page structure:
  /, /weather, /map, /about as CLAUDE.md proposes, with two refinements
  requiring Mark's sign-off: (1) the search bar lives in the shared layout,
  not just on "/", so users can re-search without leaving results, matching
  current behavior; (2) the map picker's primary interaction stays a JS
  overlay launched from wherever the user is, with "/map" as a secondary
  deep-linkable entry point rather than the only way to reach it.

Recommended database approach:
  SQL Server + EF Core, single RecentSearch entity, upsert-and-trim
  replacing the current three-statement delete/insert/trim, new nullable
  ElevationMeters column closing the one known functional gap, indexed on
  (SessionId, LastAccessedUtc).

Recommended testing strategy:
  xUnit for pure domain logic (unit conversions, WMO mapping, AQI
  categorization, forecast shaping) and integration tests (WebApplicationFactory
  + real/ephemeral SQL Server + mocked HttpClient fixtures for external
  APIs — a capability the current app has never had); Playwright for .NET
  for browser tests, deferred until the core app is stable, prioritized per
  CLAUDE.md's existing list. Do not over-invest in cosmetic/animation
  testing.

Highest migration risks:
  High: radar/map-picker interaction fidelity; first-time IIS/SQL Server
  deployment to the target environment.
  Medium: theme-engine port volume; SQL schema/migration design; formalizing
  the implicit client/server JSON contract without silent field drift.
  Low: backend domain logic; external API integration; session/recent-
  search persistence.

Recommended implementation sequence:
  CLAUDE.md's D1-D18 endorsed as-is, with one addition: a "walking
  skeleton" deployment rehearsal (minimal Razor Pages app + one EF Core
  migration, deployed to IIS on the Windows Server 2025 VM) inserted right
  after Phase C, to de-risk the deployment environment early rather than
  discovering hosting/SQL-auth/HTTPS problems only in Phase E after all
  feature work is done.

Decisions requiring Mark's approval:
  1. Should the search input be part of the shared layout (present on both
     "/" and "/weather"), rather than living only on the home page?
  2. Should "/map" remain a secondary/deep-link entry point while the
     primary map-picker interaction stays a same-page JS overlay (as
     today), rather than a full page navigation for every use?
  3. Should the RainViewer frame-metadata lookup move server-side (behind
     an IRadarService, for consistency with every other external call),
     or stay a direct browser-to-RainViewer call as it is today, accepting
     the one inconsistency in the service-isolation model?
  4. Is the standalone CLI (Atmos.Cli) worth retaining at all? Recommended
     yes (low effort, shares services with the web app), but it is
     explicitly optional-value per CLAUDE.md §35 and should be confirmed
     rather than assumed.
  5. Include the optional LocationType column on RecentSearch now (cheap,
     matches CLAUDE.md's example schema, but currently unused by any
     feature), or defer it until something actually needs it?
  6. Approve inserting an early IIS/SQL-Server deployment rehearsal into
     the implementation sequence (a structural change to CLAUDE.md's
     Phase D ordering, per CLAUDE.md §24's requirement to flag and explain
     any change that materially affects the approved plan).
```

Phase 0 and Phase A are complete. No implementation should begin until this assessment is reviewed and the six items above are resolved, per CLAUDE.md §23/§24.
