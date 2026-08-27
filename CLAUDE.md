# Atmos Weather — .NET Modernization Project Context

## 1. Project Purpose

Atmos Weather is a small weather application originally implemented as a deliberately minimal TypeScript/Node.js application.

The current application is a single-page weather application with:

* TypeScript server-side code
* Node.js built-in HTTP server
* SQLite via `better-sqlite3`
* HTML/CSS/JavaScript generated from a large server-side template literal
* No frontend framework
* No bundler
* No automated test suite
* No formal build pipeline
* Several free, keyless external weather/geospatial APIs

The purpose of this project is to **modernize Atmos Weather into a maintainable .NET application running on Windows Server/IIS with SQL Server persistence**, while preserving the application's useful functionality and visual character.

This is a modernization and architectural improvement project, **not a line-by-line translation exercise**.

The existing application is the behavioral reference implementation.

---

## 2. Modernization Objective

The target application should be:

* ASP.NET Core 10
* Razor Pages based
* SQL Server backed
* Entity Framework Core for persistence
* Hosted under IIS
* Developed primarily with the .NET CLI
* Structured into maintainable application layers without excessive enterprise abstraction
* Supported by meaningful automated tests
* Configurable by environment
* Observable through structured logging
* Resilient to failures of external weather APIs
* Responsive on desktop and mobile
* Visually faithful to the useful aspects of the existing Atmos UI

The application should retain JavaScript for browser-side functionality where JavaScript is the appropriate technology.

Do **not** interpret "port to .NET" as "rewrite all browser JavaScript in C#."

---

## 3. Current Application

The original repository is:

<https://github.com/markwilliams970/atmos-weather-spa>

Important current files include:

* `weather-server.ts` — primary web application
* `weather.ts` — standalone CLI weather tool
* `README.md` — user-facing documentation
* `Claude.md` — original engineering context
* `package.json`
* `tsconfig.json`

The original `weather-server.ts` contains essentially the entire application:

1. SQLite persistence
2. session handling
3. WMO weather-code definitions
4. TypeScript models
5. weather API integration
6. air-quality API integration
7. elevation lookup
8. nearby-place lookup
9. HTML
10. CSS
11. browser JavaScript
12. API handlers
13. HTTP routing
14. server startup

This single-file structure was intentional for the original learning project.

The .NET port is **not required to preserve this structure**.

---

## 4. Existing Functional Requirements

The current application provides the following capabilities.

### Location search

Users can:

* search by five-digit US ZIP code
* search by city name
* select an autocomplete result
* enter a ZIP directly through a URL
* select an arbitrary location using the map picker

### Map picker

The application supports:

* interactive slippy-map behavior
* pan
* zoom
* click-to-place a location
* latitude/longitude conversion
* location labeling
* elevation lookup
* nearby named-place lookup

The map picker is an important feature and must not be accidentally removed during modernization.

### Forecast

The application displays:

* current conditions
* temperature
* feels-like temperature
* humidity
* wind speed
* wind direction
* precipitation
* UV index
* sunrise/sunset
* 24-hour forecast
* 7-day forecast

### Air quality

The application displays:

* US AQI
* PM2.5
* PM10
* ozone
* NO2
* AQI category

### Radar

The application displays a weather radar map using:

* CartoDB basemap tiles
* RainViewer radar tiles
* Web Mercator tile calculations
* current location marker

The current implementation has deliberately hand-built radar tile rendering. Preserve this capability unless a clearly superior alternative is justified.

### Visual weather experience

The application contains:

* dynamic sky themes
* day/night variations
* animated stars
* rain
* snow
* clouds
* fog
* lightning
* custom SVG gauges
* custom SVG charts

These visual elements are part of the application's identity.

Do not replace them with generic framework components merely to simplify implementation.

### Recent locations

The application stores the user's most recent searches.

Current behavior:

* no user accounts
* no login
* HTTP session cookie
* recent searches are scoped to a session
* maximum of 10 recent locations
* unit preference is associated with a saved location

The .NET implementation should preserve the user experience while improving the persistence model.

### Units

The application supports:

* Imperial
* Metric

The unit preference should persist with the saved location.

---

## 5. External APIs

The current application uses the following external services.

### Zippopotam

Purpose:

* ZIP → city/state/latitude/longitude

### Open-Meteo Forecast API

Purpose:

* current conditions
* hourly forecast
* daily forecast
* elevation-aware forecast

### Open-Meteo Geocoding API

Purpose:

* city-name autocomplete

### Open-Meteo Air Quality API

Purpose:

* US AQI
* PM2.5
* PM10
* ozone
* NO2

### Open-Meteo Elevation API

Purpose:

* ground elevation for map-selected coordinates

### OpenStreetMap / Overpass

Purpose:

* nearby named geographic features

### Nominatim

Purpose:

* reverse-geocoding fallback

### RainViewer

Purpose:

* radar imagery

### CartoDB

Purpose:

* dark basemap tiles

External API behavior must be isolated behind application services/clients.

Do not allow third-party JSON response models to leak throughout the application.

---

## 6. Target Architecture

The preferred architecture is:

```text
Browser
   |
   v
ASP.NET Core / Razor Pages
   |
   +--------------------------+
   |                          |
   v                          v
Application Services       EF Core
   |                          |
   v                          v
External APIs              SQL Server
```

The preferred project structure is approximately:

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

The exact namespace/project names may be refined during Phase B.

Do not introduce additional projects unless there is a concrete architectural reason.

---

## 7. ASP.NET Architecture

Use **Razor Pages** as the primary web framework.

Do not introduce:

* Blazor Server
* Blazor WebAssembly
* React
* Angular
* Vue
* a full SPA framework

unless an explicit architectural review determines that Razor Pages cannot reasonably support a requirement.

Razor Pages should provide:

* server-rendered page structure
* routing
* model binding
* validation
* common layout
* error handling

JavaScript should provide:

* interactive maps
* charts
* radar
* autocomplete
* dynamic weather refresh
* tabs
* animations
* other browser-local interactions

This is intentionally a **hybrid server-rendered application**, not a pure SPA.

---

## 8. Page Structure

The existing SPA does not need to remain a SPA.

The preferred page model is:

| Route | Purpose |
|---|---|
| `/` | Home / Search |
| `/weather` | Weather forecast for selected location |
| `/map` | Interactive map picker |
| `/about` | Application information and data sources |

The Current / 24-Hour / 7-Day forecast views should remain tabs within the Weather page.

Do not turn each forecast tab into a separate page.

The recent-search sidebar/drawer should remain part of the shared application layout where practical.

The final page structure may change during Phase B if there is a compelling usability reason.

---

## 9. Data Model

Use SQL Server through Entity Framework Core.

The database should not be treated as a literal SQLite translation.

The primary persisted entity is expected to be similar to:

**RecentSearch**

* `Id`
* `SessionId`
* `Label`
* `Latitude`
* `Longitude`
* `ElevationMeters`
* `Units`
* `LocationType`
* `CreatedUtc`
* `LastAccessedUtc`

The exact schema must be designed during Phase B.

Important requirements:

* use UTC timestamps
* use appropriate SQL Server data types
* use indexes appropriate for session-based recent-search retrieval
* preserve the "last 10 searches per session" behavior
* persist elevation for map-selected locations
* use EF Core migrations
* never manually modify the production schema without documenting the migration

The original application's known limitation where map-selected elevation is not persisted should be corrected in the .NET version.

---

## 10. Domain Models

Create application models representing the application's concepts rather than exposing Open-Meteo's response models directly to Razor Pages.

For example:

* `WeatherForecast`
* `Location`
* `CurrentConditions`
* `HourlyForecast`
* `DailyForecast`
* `AirQuality`
* `RecentSearch`
* `RadarFrame`

External API DTOs should be separate from application/domain models.

Use explicit mapping:

```text
External API DTO
       |
       v
Application model
       |
       v
Presentation model
```

Do not pass raw JSON dictionaries or `object` values through the application.

Avoid `dynamic`.

Avoid `any`-equivalent escape hatches.

---

## 11. Service Boundaries

Prefer small, meaningful services such as:

* `IWeatherService`
* `IGeocodingService`
* `IAirQualityService`
* `IElevationService`
* `INearbyPlaceService`
* `IRadarService`
* `IRecentSearchService`

External API-specific HTTP clients may be separated where useful.

Do not create interfaces simply because "every class must have an interface."

Use abstractions when they provide:

* testability
* meaningful separation
* alternate implementations
* external-system isolation

Do not introduce a repository layer merely to wrap Entity Framework Core.

EF Core is already the persistence abstraction.

---

## 12. HTTP Client Design

Use `HttpClient` through ASP.NET Core's supported HTTP-client infrastructure.

External calls must have:

* explicit timeouts
* cancellation support
* sensible error handling
* structured logging
* appropriate retry behavior where justified

Do not blindly retry every external request.

For non-critical cosmetic lookups such as nearby-place enrichment, fail fast and allow the forecast to continue.

A failure in nearby place lookup should not prevent weather forecast from being displayed.

Similarly, failure of a nonessential enhancement should not make the entire page unavailable.

---

## 13. Configuration

Do not hardcode:

* SQL Server connection strings
* external API base URLs
* ports
* deployment-specific filesystem paths
* environment-specific settings

Use ASP.NET Core configuration.

Development configuration may use:

* `appsettings.json`
* `appsettings.Development.json`

Secrets must not be committed to Git.

Production secrets should be supplied through an appropriate configuration mechanism.

The application currently uses keyless external APIs, but this rule must remain in place because future APIs may require credentials.

---

## 14. Logging and Observability

The .NET application must use structured logging.

Log:

* application startup
* important configuration failures
* external API failures
* unexpected exceptions
* database failures
* important application events

Do not log:

* session identifiers unless there is a compelling debugging reason
* secrets
* credentials
* unnecessary personal information

Avoid silent `catch` blocks.

If an exception is intentionally suppressed, explain why in code and log at an appropriate level when useful.

---

## 15. Error Handling

External API failures must be translated into useful application behavior.

Do not expose raw third-party exception messages to end users.

The UI should distinguish between:

* invalid user input
* unavailable weather data
* unavailable optional enhancement
* unexpected application error

Use appropriate HTTP status codes for API endpoints.

Do not reproduce the original application's pattern of manually writing JSON responses and status codes everywhere.

---

## 16. HTTP/API Endpoints

The original API routes are:

* `/api/weather`
* `/api/geocode`
* `/api/recent`
* `/api/save-units`
* `/api/air-quality`
* `/api/elevation`
* `/api/nearby-place`

These are a behavioral reference, not necessarily a required final API design.

Prefer conventional HTTP semantics.

In particular:

* GET should retrieve data
* POST/PUT/PATCH should change state
* DELETE should remove state where appropriate

The original `/api/save-units` behavior uses GET for mutation. Do not reproduce this design.

Where Razor Pages can perform an operation naturally without introducing a public JSON endpoint, prefer the simpler architecture.

---

## 17. Security

Apply normal ASP.NET Core security practices.

At minimum:

* HTTPS in deployed environments
* secure cookie settings
* HttpOnly cookies where appropriate
* SameSite protection
* input validation
* output encoding
* anti-forgery protection for state-changing browser requests
* no secrets in source control
* no raw SQL unless justified
* parameterized database access

User-provided labels must never be rendered as trusted HTML.

Prefer normal Razor encoding and DOM `textContent` semantics over manual HTML escaping.

---

## 18. Testing Philosophy

Testing is a required part of this modernization.

The original application has no automated test suite.

The .NET application should have meaningful coverage for important behavior.

### Unit tests

At minimum cover:

* WMO weather-code mapping
* temperature conversions
* wind-speed conversions
* precipitation conversions
* compass-direction conversion
* AQI categorization
* coordinate calculations
* Haversine distance
* location-label formatting
* forecast transformation
* recent-search retention logic

### Integration tests

Cover:

* application startup
* database initialization/migrations
* page routing
* weather endpoints
* invalid input
* session behavior
* recent-search persistence
* unit preference persistence

External APIs should normally be tested using deterministic fixtures/mocks rather than making the test suite depend on live Internet services.

### Browser tests

Consider Playwright after the core application is stable.

Prioritize:

1. ZIP search
2. city autocomplete
3. forecast rendering
4. recent-search selection
5. unit switching
6. map picker
7. map-selected forecast
8. radar rendering

Do not spend excessive effort testing cosmetic animation details.

---

## 19. Definition of Done

A feature is not complete merely because the code compiles.

For a normal implementation task, Claude should:

1. inspect the current code
2. understand the intended behavior
3. make the smallest appropriate implementation
4. compile the solution
5. run relevant automated tests
6. fix failures
7. manually inspect affected UI where practical
8. report what changed
9. identify any remaining limitations

Do not declare success based solely on compilation.

---

## 20. Build Requirements

The primary development environment should use:

* .NET 10 SDK
* `dotnet` CLI
* Git
* SQL Server
* SQL Server Management Studio or an equivalent SQL tool
* an editor/IDE suitable for C# and Razor

The Windows Server 2025 VM is the intended deployment environment.

The development environment does not have to be identical to the production VM.

Prefer a normal developer workstation for compilation and debugging, with the Windows Server VM used for SQL Server/IIS integration and deployment testing.

---

## 21. IIS Deployment

The target hosting architecture is:

```text
Internet / LAN
     |
     v
    IIS
     |
     v
ASP.NET Core 10
     |
     +----> SQL Server
     |
     +----> External Weather APIs
```

Use the ASP.NET Core Hosting Bundle on the Windows Server host.

Publish using:

```shell
dotnet publish --configuration Release
```

Prefer framework-dependent deployment unless there is a concrete reason to use self-contained deployment.

The generated `web.config` should normally be produced by the .NET publish process rather than manually maintained.

Deployment should include:

* IIS site configuration
* application pool configuration
* filesystem permissions
* database connection configuration
* HTTPS configuration where applicable
* logging
* smoke testing

---

## 22. SQL Server

The application should use a dedicated SQL Server database.

The database should be created and evolved through EF Core migrations.

Do not rely on `CREATE TABLE IF NOT EXISTS` style runtime schema manipulation.

Database changes should be represented by migrations and committed to source control.

During development, migrations may be applied with EF tooling.

During deployment, database migration strategy must be explicit.

---

## 23. Development Phases

This project is intentionally divided into phases.

### Phase 0 — Baseline

Before modifying the application:

* run the existing application
* exercise every feature
* record expected behavior
* identify existing defects
* capture screenshots if useful
* create a feature checklist

Do not begin implementation until the baseline is understood.

### Phase A — Current Architecture Review

Produce a written assessment of:

* application structure
* data flow
* external APIs
* persistence
* session model
* frontend behavior
* JavaScript architecture
* known defects
* technical debt
* portability concerns

Do not make major implementation changes during this phase.

### Phase B — Target Architecture

Produce a detailed porting plan covering:

* .NET version
* Razor Pages architecture
* page structure
* JavaScript structure
* SQL schema
* EF Core model
* service boundaries
* configuration
* logging
* error handling
* security
* testing
* deployment

This phase must explicitly decide which existing behavior will be:

* preserved
* improved
* removed
* deferred

Do not begin large-scale implementation until this plan has been reviewed.

### Phase C — Build Environment

Establish and validate:

* .NET SDK
* project scaffolding
* SQL Server connectivity
* EF Core tooling
* development configuration
* test execution
* IIS deployment prerequisites

The environment should be proven with a minimal working application before the full port begins.

### Phase D — Implementation

Implement incrementally.

Suggested sequence:

1. Solution/project skeleton
2. Configuration and logging
3. SQL Server + EF Core
4. Session/recent-search persistence
5. External API clients
6. Weather domain/application services
7. Basic forecast page
8. Search/autocomplete
9. Units
10. Air quality
11. Map picker
12. Radar
13. Dynamic weather themes
14. Responsive/mobile refinement
15. CLI functionality if retained
16. Integration tests
17. Browser tests
18. Documentation and cleanup

This sequence may change if Phase B determines that another order is safer.

### Phase E — Deployment and Refinement

Deploy to IIS on the Windows Server 2025 VM.

Then verify:

* application startup
* SQL Server connectivity
* IIS routing
* HTTPS
* external API connectivity
* weather search
* recent searches
* unit persistence
* map picker
* elevation
* nearby-place lookup
* air quality
* radar
* mobile layout
* logging
* error handling

Fix deployment-specific problems before declaring the migration complete.

---

## 24. Phase Gates

Claude must not silently skip phases.

At the end of each major phase, provide:

* Phase:
* Status:
* Completed:
* Findings:
* Decisions:
* Open questions:
* Risks:
* Recommended next phase:

If an architectural decision materially changes the approved plan, stop and explain the proposed change before implementing it.

---

## 25. AI-Assisted Development Rules

This project is intentionally being developed with Claude Code.

Claude is expected to behave as a senior software engineer, not as an autonomous code generator.

Before modifying unfamiliar code:

* inspect it
* understand its purpose
* identify dependencies
* identify tests
* determine whether the requested change fits the architecture

Do not make broad speculative refactors.

Do not rewrite working code merely because another style is preferred.

Do not add libraries without justification.

Do not introduce architectural patterns merely because they are fashionable.

Prefer the simplest design that satisfies the requirement.

---

## 26. Avoid Overengineering

This is a small weather application.

Do not introduce:

* CQRS unless a real requirement emerges
* MediatR unless a real requirement emerges
* a generic repository layer around EF Core
* event sourcing
* microservices
* distributed caching
* message queues
* Kubernetes
* unnecessary dependency injection abstractions
* excessive project fragmentation

The goal is a **clean monolithic ASP.NET Core application**.

A well-structured monolith is the preferred architecture.

---

## 27. Preserve Important Existing Behavior

The following are considered important application characteristics:

* fast weather lookup
* simple location search
* arbitrary map location selection
* elevation-aware map forecasts
* useful recent-search behavior
* current/hourly/daily forecast views
* air-quality information
* radar
* visually distinctive weather themes
* responsive design
* no account requirement

Do not remove a feature simply because it is inconvenient to port.

If a feature is genuinely problematic, document the issue and propose alternatives.

---

## 28. Improvements Explicitly Encouraged

The modernization should improve the following known weaknesses of the original application:

* automated testing
* structured logging
* configuration management
* external API timeouts
* external API resilience
* HTTP semantics
* security
* persistence model
* database migrations
* separation of concerns
* client/server contracts
* duplicated domain logic
* map-selected elevation persistence
* deployment reproducibility

The goal is not merely to reproduce technical debt in C#.

---

## 29. External API Resilience

External weather services are outside the application's control.

Design accordingly.

Use:

* cancellation tokens
* timeouts
* appropriate retry policies
* clear failure handling
* logging
* graceful degradation

Do not allow a failure of an optional API to prevent the core forecast from being displayed.

For example:

```text
Weather API succeeds
Elevation API fails
        ↓
Display forecast
Display location
Display "elevation unavailable"
```

is preferable to:

```text
Elevation API fails
        ↓
Entire weather page fails
```

---

## 30. Data Freshness and Caching

Do not introduce aggressive weather-data caching without understanding the product requirements.

The original application's goal is timely weather information.

If caching is introduced, document:

* what is cached
* cache duration
* invalidation behavior
* whether the cached value is acceptable for the UI

Do not sacrifice timely weather information merely for reducing external API calls.

---

## 31. Frontend Strategy

Move the current monolithic browser JavaScript into maintainable modules.

Expected direction:

```text
wwwroot/js/
    app.js
    search.js
    weather.js
    charts.js
    radar.js
    map-picker.js
    themes.js
```

Use modern browser JavaScript.

The original ES5 restriction exists because the browser code was embedded inside a TypeScript template literal.

That restriction should disappear in the .NET application.

Do not introduce a frontend framework simply because modular JavaScript is being introduced.

---

## 32. CSS Strategy

Move CSS out of the server-side HTML template.

Preserve the existing design language where practical:

* CSS custom properties
* glass-like cards
* responsive layout
* weather-specific themes
* desktop sidebar
* mobile navigation
* animated weather layers

Use normal static assets under `wwwroot`.

---

## 33. Client/Server Contract

Any JSON endpoint used by browser JavaScript must have an explicit model.

Avoid undocumented JSON structures.

Prefer C# request/response models.

Where practical, document important endpoint contracts.

Do not silently rename JSON fields used by the browser without updating the client and tests.

---

## 34. Browser and Server Responsibilities

Server responsibilities:

* external API access
* persistence
* session management
* validation
* application/business logic
* weather response transformation
* security

Browser responsibilities:

* presentation
* interaction
* map rendering
* chart rendering
* animations
* tab navigation
* autocomplete presentation
* dynamic updates

Avoid duplicating business rules in both places.

If the browser needs a value for presentation, expose it explicitly rather than reimplementing server-side domain logic unnecessarily.

---

## 35. CLI

The original repository contains a standalone `weather.ts` CLI.

During Phase B, explicitly decide whether the .NET version should retain this capability.

If retained, prefer `Atmos.Cli` only if the CLI provides meaningful value.

Do not create a separate project solely to reproduce a feature that is not useful.

If the CLI is retained, share domain/application services with the web application rather than duplicating weather-code tables and conversion logic.

---

## 36. Documentation

Update documentation as the migration progresses.

At minimum maintain:

* `README.md`
* `ARCHITECTURE.md`
* `DEPLOYMENT.md`

The README should explain:

* what Atmos is
* how to build it
* how to run it
* how to configure it
* how to run tests

Architecture documentation should explain the major design decisions.

Deployment documentation should explain IIS and SQL Server deployment.

---

## 37. Git Discipline

Make focused commits.

Prefer commits such as:

* Create ASP.NET Core solution skeleton
* Add SQL Server persistence
* Implement weather service
* Implement forecast page
* Add recent-search persistence
* Implement map picker
* Add integration tests
* Prepare IIS deployment

Avoid giant commits containing unrelated changes.

Do not commit:

* database files
* secrets
* local configuration containing credentials
* generated build artifacts unless explicitly required
* IDE-specific junk

---

## 38. Definition of Migration Complete

The migration is complete when:

* the ASP.NET Core application builds successfully
* automated tests pass
* SQL Server persistence works
* the application runs under IIS
* the major existing features work
* the map picker works
* radar works
* elevation-aware forecasts work
* recent searches work
* unit preferences work
* the application works on desktop and mobile
* external API failures degrade gracefully
* logs are useful for diagnosing failures
* deployment is documented
* the architecture is substantially more maintainable than the original implementation

A successful migration is **not** defined by having the same source-code structure as the TypeScript application.

It is defined by preserving useful behavior while producing a cleaner, testable, maintainable .NET application.

---

## 39. Claude's Operating Rule

When uncertain, prefer:

1. preserving existing user-visible behavior
2. the simplest maintainable implementation
3. explicit types
4. automated tests
5. clear separation of external systems from application logic
6. ordinary ASP.NET Core conventions
7. minimal dependencies
8. incremental changes

If a decision could materially alter the architecture, stop and ask for confirmation rather than silently making the decision.

Do not confuse "I can implement this" with "I should implement this without review."

This project is a collaborative modernization effort.

