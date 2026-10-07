# portfolioweb

Web UI for Portfolio Monitor. React + Vite (JavaScript), no runtime dependencies beyond React.

## Status

Two pages, switched by a hash route (`#/dashboard`, `#/import`) so deep links and browser
back/forward work without a router dependency.

The dashboard reads **real stored data** from the `portfolioapp` API — holdings, watchlist,
prices, allocation, signal scores, and alert history all come from the database. Sign-in is still
**mocked in the browser** (see below).

Nothing is faked: a value the backend does not have yet renders as `—` with an explanation,
rather than a zero or a placeholder number.

## Run

```bash
npm install
npm run dev      # http://localhost:5173
npm run build
npm run lint
```

Sign in with `demo` / `portfolio123`.

The dashboard needs the API. In a second terminal:

```bash
cd portfolioapp
dotnet build
cd bin/Debug/net8.0
func host start --port 7234
```

The dev server proxies `/api` to `http://localhost:7234`, so the browser stays same-origin and no
CORS configuration is needed. Copy `.env.example` to `.env.local` to point at a different backend.

Note: `func start` can fail from the project folder with "Expected 1 .csproj but found 2" because
of a generated `obj/.../WorkerExtensions.csproj`. Running it from `bin/Debug/net8.0` avoids that.

## Where the data comes from

| Panel | Source |
| --- | --- |
| Summary | `Holdings` + latest `PriceSnapshots`, open `Signals` |
| Prices | Latest `PriceSnapshots` per symbol, falling back to the price the broker file carried |
| Holdings | `Holdings` joined to `Accounts` |
| Allocation | Computed from holdings; limits come from `AllocationAbove` rules |
| Watchlist | `WatchlistSymbols` with thresholds derived from `PriceBelow` / `PriceAbove` rules |
| Opportunity scores | Open `Signals` — score and facts as the rule engine stored them |
| Alert history | `Alerts` joined to `Signals` |

`GET /api/dashboard` returns all of it as one snapshot. That is deliberate: a dashboard whose
panels are fetched separately can render totals from one instant beside prices from another.

**Price honesty.** Every quote records whether it came from the market-data provider or from a
broker import, and the header reports the *weakest* source present. A single import-priced symbol
keeps the whole board labelled "Prices from import" rather than implying live data.

**Review actions.** Acknowledging or ignoring a signal posts to
`/api/signals/{id}/status` and then re-reads the snapshot, because the server also acknowledges the
linked alerts — patching locally would leave the alert history disagreeing with the signal.

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

## Import

`#/import` has two modes: **Upload CSV** and **Add manually**.

### Add manually

For tracking a few positions without exporting anything from a broker. Only **symbol** and
**shares** are required; average cost and account are optional.

- The symbol is normalised (`msft` → `MSFT`) and must be a tradable ticker — cash sweeps like
  `FCASH**` are rejected, because the monitoring run could never price them.
- Re-adding a symbol you already hold **updates** its share count rather than creating a
  duplicate, keyed on (account, symbol) exactly like the CSV import.
- Leaving average cost blank on an update **keeps** the cost you entered before. Correcting a
  share count should not silently destroy your own cost basis.
- Blank average cost on a new position means "not tracked": unrealized gain reads as unknown
  instead of being guessed.
- No market price is invented. A manual position has no value until the monitoring run fetches a
  quote for it.
- The symbol is added to the watchlist (`Source = Manual`) so it starts being monitored.
- Positions can be removed, which deletes the holding but **keeps** the watchlist entry — selling
  out of a position is not a reason to stop tracking the stock.

Manually entered rows are stored with a null `LastImportId` rather than a synthetic import
record, so the import audit trail keeps meaning only what it says. The manage table badges each
row **Manual** or **Imported**.

### Upload CSV

Accepts a broker positions export (`.csv`) by drag-and-drop or file picker.

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

See "Run" above — the dashboard requires the API, so both processes are needed.

## What the dashboard shows

| Section | Contents |
| --- | --- |
| Summary | Portfolio value, day change, unrealized gain, open signal count |
| Prices | Last price, day change, day range, volume, RSI 14, SMA 50/200, and the source of each quote |
| Holdings | Quantity, average cost, price, day change, market value, unrealized gain, weight |
| Allocation | Share of portfolio value per position, against limits from allocation rules |
| Watchlist | Rule-derived buy/sell thresholds, distance to each, rule state, opportunity score |
| Opportunity scores | Open signals with the facts the rule engine stored, plus acknowledge/ignore |
| Alert history | Alerts joined to their signal: direction, score, rule version, delivery and acknowledgement |

Indicators (RSI, moving averages) need price history, so they read `—` until the monitoring run
has stored enough snapshots. The panel says so rather than leaving a blank.

## Layout

```text
src/
  lib/portfolioApi.js   portfolioapp API client (dashboard read, CSV import, signal review)
  lib/auth.js           mock authentication and session storage
  lib/csv.js            RFC 4180 CSV reader and broker amount/percent parsing
  lib/importPositions.js  CSV -> normalized holdings, validation, preview payload
  lib/format.js         currency/percent/date formatting, including "unknown" handling
  lib/derive.js         API snapshot -> positions, totals, watchlist rows, score breakdowns
  hooks/useSession.js       sign in/out, session restore and expiry
  hooks/usePortfolioData.js snapshot state, manual refresh, auto-refresh timer
  hooks/useHashRoute.js     two-page hash routing
  components/           Header, Nav, LoginScreen, MarketStatus, RefreshControls,
                        ImportPage, ManualEntry, SummaryCards, PriceBoard, HoldingsTable,
                        AllocationPanel, WatchlistPanel, ScorePanel, AlertHistory,
                        EmptyState, Delta
  App.jsx               auth gate, routing, page composition
  App.css               styles
```

`src/lib/derive.js` is pure — the same snapshot always renders the same view — and every component
takes its data through props, so the API shape is the only thing that has to change when the
backend evolves.

## Not implemented yet

Real authentication (`src/lib/auth.js` is the seam), rule editing from the UI (the API supports
`POST /api/rules`, but there is no screen yet), a dedicated signal-detail route, "mark for
follow-up" as a third review action, notification preferences, and tax lots.

Live prices depend on the monitoring run: until it stores snapshots, the dashboard falls back to
the prices carried in the imported file and labels them as such.

Signals are recommendations for review, not orders.
