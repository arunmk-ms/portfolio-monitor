# portfolioweb

Web UI for Portfolio Monitor. React + Vite (JavaScript), no runtime dependencies beyond React.

## Status

MVP dashboard for two symbols (MSFT, NVDA). Sign-in is **mocked in the browser** and market data
is **simulated locally** — no network calls leave the app. Per `DESIGN.md`, this UI will read from
the `portfolioapp` Web API and must never call a market-data provider directly.

## Run

```bash
npm install
npm run dev      # http://localhost:5173
npm run build
npm run lint
```

Sign in with `demo` / `portfolio123`.

## Sign-in

`src/lib/auth.js` checks credentials against a hardcoded user and issues a fake token. **Replace it
before this app is exposed to anyone**: browser-side credential checks are not security. The module
already has the shape the real thing needs, so swapping `signIn()` for an API call is the only
change required.

- Session is held in `localStorage` when "Keep me signed in" is checked, otherwise `sessionStorage`.
- Sessions expire after 8 hours and the UI drops them the moment they do.
- Only a token and profile fields are stored — never the password.
- Signing out clears storage and stops all polling.

## Refresh

The header carries a **Refresh** button and an **Auto** toggle with a 15s / 30s / 1m / 5m interval.

- The countdown to the next automatic refresh is always visible.
- Auto-refresh pauses while the browser tab is in the background and resumes on return.
- A refresh already in flight is never duplicated, by the button or the timer.
- A failed refresh keeps the last good data on screen and shows a retry banner.
- The toggle and interval persist across reloads.

Because there is no backend yet, `src/lib/api.js` applies a small random walk to the seed quotes on
each refresh and re-runs the threshold rules, so refreshing produces genuinely new numbers. It also
deduplicates signals: a standing condition raises one alert, and only re-alerts after the condition
clears and crosses again.

## Pages

Two pages, switched by a hash route (`#/dashboard`, `#/import`) so deep links and browser
back/forward work without a router dependency.

## Import

`#/import` accepts a broker positions export (`.csv`) by drag-and-drop or file picker.

The file is previewed locally first — parsed in the browser so you can check it before anything
leaves the page. Choosing **Save to portfolio** uploads the original file to the backend, which
re-parses it server-side and stores the result. The server is the source of truth; the preview is
only a pre-flight check.

What gets written (see `portfolioapp`):

| Table | Contents |
| --- | --- |
| `PortfolioImports` | One audit row per upload: file name, row counts, and the broker's "Date downloaded" stamp |
| `Accounts` | Created on demand, keyed by account **name** |
| `Holdings` | Upserted by (account, symbol), so re-uploading updates rather than duplicates |
| `WatchlistSymbols` | New equity tickers, marked `Source = Import` |

Cash and money-market rows (`FCASH**`) are stored as holdings but flagged `IsMonitorable = false`
so the quote monitor never spends API quota on them.

Account numbers are never read. The column is dropped in the browser parser and, independently,
by the server parser, so it cannot reach the database, logs, or telemetry. It is surfaced in the
Notes panel as `Column "Account number" was not read.` so the exclusion is visible.

The parser handles the awkward parts of real broker exports:

- UTF-8 byte order marks, `CRLF`, and quoted fields containing commas (`"$3,567.50 "`)
- Accounting negatives (`($39,573.87)`), currency symbols, and trailing spaces
- Cash and money-market rows (`FCASH**`), which are separated from equity positions
- Blank rows, multi-line legal disclaimers, and the trailing "Date downloaded" line
- Fractional share quantities

Rows it cannot use are skipped rather than silently dropped — missing or invalid tickers, missing
or negative quantities — and each one is reported with its line number. It also flags duplicate
symbols, positions with no cost basis, and rows where quantity x price disagrees with the stated
value.

### Running against the backend

```bash
# terminal 1 - API (see portfolioapp/README.md for database setup)
cd portfolioapp
func start --port 7234

# terminal 2 - UI
cd portfolioweb
npm run dev
```

The dev server proxies `/api` to `http://localhost:7234`, so the browser stays same-origin and no
CORS configuration is needed. Copy `.env.example` to `.env.local` to point at a different backend.

Note: the Functions host may fail to start from the project folder with "Expected 1 .csproj but
found 2" because of a generated `obj/.../WorkerExtensions.csproj`. If that happens, run
`dotnet build` then `func host start --port 7234` from `bin/Debug/net8.0`.

## What the dashboard shows

| Section | Contents |
| --- | --- |
| Summary | Portfolio value, day change, unrealized gain, open signal count |
| Prices | Last price, day change, day/52-week range, volume vs average, RSI 14, SMA 50/200 |
| Holdings | Quantity, average cost, price, day change, market value, unrealized gain, weight |
| Allocation | Actual vs target weight per position with drift against a 5% limit |
| Watchlist | Buy/sell thresholds, distance to each threshold, rule state, opportunity score |
| Opportunity scores | 0–100 score per symbol with the factors behind it (value, momentum, trend, volume) |
| Alert history | Alert timeline joined to its signal: direction, score, structured facts, rule version, acknowledgement |

The header surfaces market state and data quality (for example "Delayed 15 min") so stale or
delayed data is always visible.

## Layout

```text
src/
  data/portfolio.js     hardcoded seed quotes, holdings, watchlist, signals, alerts
  lib/api.js            mock Web API: snapshot fetch, price simulation, rule engine
  lib/portfolioApi.js   real portfolioapp API client (CSV import)
  lib/auth.js           mock authentication and session storage
  lib/csv.js            RFC 4180 CSV reader and broker amount/percent parsing
  lib/importPositions.js  CSV -> normalized holdings, validation, preview payload
  lib/format.js         currency/percent/date formatting helpers
  lib/derive.js         snapshot -> positions, totals, watchlist rows, score breakdowns
  hooks/useSession.js       sign in/out, session restore and expiry
  hooks/usePortfolioData.js snapshot state, manual refresh, auto-refresh timer
  hooks/useHashRoute.js     two-page hash routing
  components/           Header, Nav, LoginScreen, MarketStatus, RefreshControls,
                        ImportPage, SummaryCards, PriceBoard, HoldingsTable,
                        AllocationPanel, WatchlistPanel, ScorePanel, AlertHistory, Delta
  App.jsx               auth gate, routing, page composition
  App.css               styles
```

## Replacing the mock backend

CSV import is wired to the real API. Market data is not yet: `src/lib/api.js` is still the seam for
that. `fetchSnapshot()` returns one timestamped snapshot containing quotes, holdings, watchlist,
signals, and alerts; point it at the real endpoint and nothing else changes. `src/lib/derive.js` is
pure — the same snapshot always renders the same view — and the components take everything through
props.

Seed shapes in `src/data/portfolio.js` match the core entities in `DESIGN.md` (`Holding`,
`WatchRule`, `PriceSnapshot`, `Signal`, `Alert`).

## Not implemented yet

Real authentication, reading stored holdings back into the dashboard (it still renders seed data),
symbol/holding/threshold management, signal detail route, review actions (acknowledge, ignore,
follow-up), notification preferences, and tax lots.

Signals are recommendations for review, not orders.
