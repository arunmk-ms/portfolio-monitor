# Portfolio Monitor

Personal portfolio and watchlist monitoring with deterministic, versioned buy/sell rules.

It polls quotes during market hours, stores price history, calculates indicators and allocation,
evaluates rules, and raises deduplicated signals with the facts behind them.

**It is decision support. It never places a trade, and nothing in it talks to a broker.** Signals
are `BUY_CANDIDATE` / `SELL_CANDIDATE` / `REBALANCE` / `INFORMATIONAL` prompts for review.

## Contents

| Path | What it is |
| --- | --- |
| [`portfolioapp/`](portfolioapp/README.md) | Azure Functions app (.NET 8, isolated worker) — timer-driven monitoring, rule engine, and the HTTP API |
| [`portfolioweb/`](portfolioweb/README.md) | React + Vite dashboard |
| [`portfolioapp.Tests/`](portfolioapp.Tests) | xUnit tests for the backend |
| [`deploy/`](deploy/README.md) | One-command Azure deployment script (`Deploy-PortfolioMonitor.ps1`) |
| [`DESIGN.md`](DESIGN.md) | Product design, core entities, and delivery phases |

## How it fits together

```text
                Alpha Vantage (GLOBAL_QUOTE)
                          |
                          v
  timer ---> QuoteMonitorFunction ---> PriceSnapshot ---.
              (market-hours gated)                      |
                          |                             v
                          '------> PortfolioAnalysisService
                                   indicators + allocation
                                            |
                                        RuleEngine
                                            |
                                    Signal (deduplicated)
                                            |
                                    INotificationChannel
                                            |
        portfolioweb  <---  HTTP API  <---  Azure SQL
```

One Functions app and one Azure SQL database. No queue — a run comfortably finishes inside the
timer interval at this symbol count.

## Quick start

Prerequisites: .NET 8 SDK, Azure Functions Core Tools v4, Node.js LTS, Azurite, a SQL Server
instance (LocalDB is fine), and a free [Alpha Vantage API key](https://www.alphavantage.co/support/#api-key).

```powershell
# 1. database
cd portfolioapp
$env:ConnectionStrings__PortfolioDb = "Server=(localdb)\MSSQLLocalDB;Database=portfoliomonitor;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update

# 2. settings — copy the sample and add your API key
Copy-Item local.settings.sample.json local.settings.json

# 3. storage emulator (the timer needs it for schedule state)
azurite --silent --location $env:TEMP\azurite-portfolioapp

# 4. backend
func start --port 7234

# 5. frontend (separate terminal)
cd portfolioweb
npm install
npm run dev      # http://localhost:5173, proxies /api to :7234
```

Sign in to the UI with `demo` / `portfolio123` — authentication is still mocked in the browser.

Load a portfolio by dropping a broker positions export (`.csv`) on the `#/import` page. Account
numbers are never read: the column is dropped by the browser parser and, independently, by the
server parser.

## Running the tests

```powershell
dotnet test portfolioapp.Tests
cd portfolioweb; npm run lint
```

## HTTP API

Served by `portfolioapp`; all routes are under `/api`.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Readiness probe — config, database connectivity, migrations |
| `POST` | `/portfolio/import` | Upload a broker positions CSV |
| `GET` | `/holdings` | Current positions with market value and allocation |
| `GET` | `/signals` | Open and historical signals with their facts |
| `POST` | `/signals/{id}/status` | Acknowledge, ignore, or flag a signal |
| `GET` | `/rules` | List watch rules |
| `POST` | `/rules` | Create or update a watch rule |

## Rules

Rules are a closed set of typed conditions rather than a stored expression language, so every
evaluation is deterministic and explainable after the fact. Each rule carries a `Version` that is
copied onto every signal it produces, so a historical alert stays explainable after its threshold
changes.

`PriceBelow`, `PriceAbove`, `PercentDropFromAverageCost`, `PercentGainFromAverageCost`, `RsiBelow`,
`RsiAbove`, `PriceBelowMovingAverage`, `PriceAboveMovingAverage`, `AllocationAbove`,
`AllocationBelow`.

A rule with no `Symbol` applies to every monitored symbol.

Signals are deduplicated on symbol + rule + direction: a standing condition raises one signal and
only re-arms once the condition clears.

## Choose an interval that matches your data tier

This is the single setting most likely to cost you. Alpha Vantage's free tier is **25 requests/day**
and returns end-of-day data, so polling every five minutes mostly re-fetches the same quote.

| Tier | `QuoteMonitorSchedule` | `MarketHours__Enabled` |
| --- | --- | --- |
| Free (end-of-day) | `0 30 21 * * 1-5` — once, after the close | `false` |
| Premium, 15-min delayed | `0 */15 13-21 * * 1-5` | `true` |
| Premium, realtime | `0 */5 13-21 * * 1-5` | `true` |

The cron window is only a coarse filter. `UsEquityMarketCalendar` makes the real open/close/holiday
decision in exchange-local time, before any HTTP call, so a closed-market tick costs no quota.
Holiday dates are configuration and currently cover **2026–2027** — extend them before January 2028.

See [`portfolioapp/README.md`](portfolioapp/README.md) for the full settings reference.

## Deploying

```powershell
cd deploy
./Deploy-PortfolioMonitor.ps1 -SubscriptionId '<your-subscription-id>'
```

Idempotent, runs from your machine, and needs no build pipeline or service principal. It provisions
storage, Log Analytics + Application Insights, Azure SQL Basic, a Flex Consumption Function App, a
free Static Web App, and a budget alert — roughly **$6–10/month**.

No database password exists anywhere in the design: the SQL server is Entra-only, the Function App
authenticates with a system-assigned managed identity, and migrations are applied from your machine
over an access token. The Alpha Vantage key is the only secret, and it lives in Function App
settings. Details in [`deploy/README.md`](deploy/README.md).

Migrations are applied **explicitly**, never at startup — `portfolioapp/Data/Scripts/migrate.sql` is
generated with `--idempotent` and is safe to re-run.

## Current state

Working end to end: CSV import, market-hours-gated quote polling, snapshot storage with duplicate
suppression, indicators and allocation, rule evaluation, signal deduplication, and the read/write
HTTP API. Alerts are delivered through `INotificationChannel`, currently wired to a logging channel
— email and push are not connected yet.

Not there yet: real authentication (the UI's sign-in is a browser-side mock), reading stored
holdings and signals back into the dashboard (it still renders seed data from `src/data/portfolio.js`),
tax lots, market events such as earnings and dividends, and AI-generated signal explanations.

Batched quote fetching is also still one request per symbol; Alpha Vantage's premium
`REALTIME_BULK_QUOTES` endpoint would collapse that into a single call.

> `DESIGN.md` predates the rules/signals/alerts work and its status tables are out of date. Trust
> this section and the per-project READMEs.

## Guardrails

- Licensed market-data provider only — no scraping of public finance sites.
- Delayed and end-of-day data is labelled wherever a price is shown.
- Market-close, provider outage, stale-data, and duplicate-alert states are surfaced, not hidden.
- Broker account numbers are never ingested, so they cannot reach the database, logs, or telemetry.
- No trading credentials. Any future broker order would sit behind explicit confirmation and a
  separate execution boundary.
- Output is informational, not tax or investment advice.
