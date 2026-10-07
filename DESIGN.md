# Portfolio Monitor Design

## Goal

Monitor a personal portfolio and watchlist, evaluate predefined buy/sell strategies, and send useful alerts when a meaningful condition occurs. The system provides decision support; it does not make autonomous trades.

## Repository Responsibilities

### `portfolioweb` - Web UI

- Display holdings, watchlist, prices, allocation, scores, and alert history.
- Let the user manage symbols, holdings, tax lots, thresholds, and notification preferences.
- Show the rule evaluation and market context behind each alert.
- Provide review actions: acknowledge, ignore, or mark for follow-up.
- Read data through the backend API. It must not call the market-data provider directly.

Status: the dashboard reads everything from `GET /api/dashboard`, and acknowledge/ignore post to
`POST /api/signals/{id}/status`. Positions can be added by hand (`POST /api/holdings`) as well as
imported from a broker file. Still to do: editing rules and thresholds from the UI, a "follow-up"
review state, notification preferences, and tax lots. Authentication is mocked in the browser and
must be replaced before the UI is exposed to anyone.

### `portfolioapp` - Azure Functions app

- Run on a timer during market hours, initially every 1-5 minutes.
- Fetch quotes in batches from a licensed market-data provider.
- Collect slower-changing data separately: fundamentals, news, earnings, dividends, and analyst events.
- Calculate technical indicators and portfolio allocation.
- Evaluate versioned, deterministic rules.
- Persist signals and suppress duplicate alerts until a condition resets.
- Send email/push notifications and expose APIs used by the web UI.
- Optionally generate an explanation from structured signal data using an AI service. AI output may explain a signal but cannot create or approve a trade.

#### Status against these responsibilities

| # | Responsibility | Status | Gap |
| --- | --- | --- | --- |
| 1 | Timer during market hours, every 1-5 min | Done | Interval is tier-bound, not code-bound |
| 2 | Fetch quotes **in batches** | Accepted as-is | One request per symbol. At the planned 8-10 symbols this is ~11s per run, so bulk fetching is not worth a premium dependency. Revisit past ~30 symbols. |
| 3 | Slower-changing data (fundamentals, news, earnings, dividends) | Not started | No second timer, no `MarketEvent` |
| 4 | Technical indicators and allocation | Done | RSI, moving averages, weighted cost basis, allocation |
| 5 | Evaluate versioned, deterministic rules | Done | `WatchRule` + `RuleEngine`; version snapshotted onto each signal |
| 6 | Persist signals, suppress duplicates until reset | Done | Open signal suppresses repeats; clearing the condition re-arms the rule |
| 7 | Send email/push notifications | Deferred by choice | `Alert` is persisted and dispatched through `INotificationChannel`; only the log channel is wired. Adding email means registering one more channel. |
| 8 | Expose APIs used by the web UI | Done | Signals, holdings, rules, import, health |
| 9 | AI explanation of signals | Not started | Explicitly optional (Phase 3) |

Remaining work is item 3 and item 9. Items 2 and 7 are deliberate decisions, not gaps.

## MVP Architecture

```text
Market data provider
          |
          v
Timer-triggered Functions ---> Rules ---> Signals ---> Notifications
          |                    |
          v                    v
       Database <---------- Web API <---------- portfolioweb
```

Start with one Functions app and one database. Add a queue only when a run can no longer complete within the timer interval or when data sources need independent retries.

Suggested Azure services:

- Azure Functions: scheduled jobs and HTTP API.
- Azure Table Storage or Cosmos DB serverless: portfolio, rules, snapshots, signals, and alert history.
- Application Insights: execution logs, provider failures, alert counts, and latency.
- Email provider or Azure Communication Services: notifications.
- Key Vault or managed application settings: provider credentials and secrets.

## Core Data

```text
Holding       userId, symbol, quantity, averageCost, account
TaxLot        holdingId, quantity, costBasis, acquiredAt
WatchRule     symbol/scope, conditions, action, enabled, version
PriceSnapshot symbol, price, volume, capturedAt
MarketEvent   symbol, type, scheduledAt, source, summary
Signal        symbol, ruleId, direction, score, facts, createdAt, status
Alert         signalId, channel, sentAt, acknowledgedAt
```

### Implementation status

| Entity | Status | Notes |
| --- | --- | --- |
| `PriceSnapshot` | Done | Written each monitoring run; unchanged quotes are not re-stored |
| `Holding` | Done | Plus `Account`, `PortfolioImport`, `WatchlistSymbol` from CSV upload. Also entered by hand via `POST /api/holdings`; manual rows carry a null `LastImportId` so the import audit trail stays truthful |
| `WatchRule` | Done | Typed conditions, versioned; version bumps when behaviour changes |
| `Signal` | Done | Open until the condition clears; carries the facts that produced it |
| `Alert` | Done | Persisted per channel; failures recorded rather than discarded |
| `TaxLot` | Pending | Not in the positions export; needs a lot-level file or manual entry |
| `MarketEvent` | Pending | Phase 2 (earnings/dividend awareness) |

Single-user for now: `Holding.userId` is deferred until authentication exists. Accounts are
identified by display name because broker account numbers are deliberately never ingested.

`facts` should contain structured values such as current price, threshold, RSI, moving averages, allocation, volume ratio, and upcoming events. Store the rule version with every signal so historical alerts remain explainable after a rule changes.

## Processing Flow

1. The timer starts a monitoring run and obtains the active symbols.
2. The app fetches batched quotes and writes a timestamped snapshot.
3. It enriches symbols with cached technical, fundamental, portfolio, and event data.
4. The rule engine evaluates conditions and produces zero or more signals.
5. A signal is deduplicated using symbol, rule, direction, and active condition state.
6. New signals are persisted, explained from their structured facts, and sent to the configured channels.
7. The web UI reads the signal, facts, and evaluation history for review.

## Initial Rules

- Price below a buy threshold.
- Price above a sell threshold.
- Percentage move from a high/low or average cost.
- Moving-average or RSI condition.
- Portfolio allocation above or below a limit.
- Optional event warning, such as earnings within a configurable number of days.

Rules should produce `BUY_CANDIDATE`, `SELL_CANDIDATE`, `REBALANCE`, or `INFORMATIONAL` signals. These are recommendations for review, not orders.

## Web Screens

- **Dashboard:** portfolio value, allocation, open signals, and market status. *Built* — also
  carries prices, holdings, watchlist, scores, and alert history, and is the only read surface
  today.
- **Watchlist:** symbol, price, change, rule state, and opportunity score. *Built* as a dashboard
  panel rather than a separate screen.
- **Signal detail:** why it triggered, supporting facts, risks/events, and alert history. *Partly
  built* — the facts behind each open signal appear inline on the dashboard; there is no dedicated
  route.
- **Portfolio:** holdings, cost basis, tax lots, and allocation drift. *Partly built* — holdings
  and cost basis are on the dashboard; tax lots are not modelled yet.
- **Import:** upload a broker positions export, preview it, then store it — or add positions by
  hand with just a symbol and share count. *Built.*
- **Rules and settings:** strategy conditions, notification channels, and provider status. *Not
  built* — the API supports reading and writing rules, but no screen uses it.

## Delivery Phases

1. **MVP:** holdings/watchlist, batched prices, threshold rules, email alerts, alert history, and dashboard.
2. **Analysis:** multiple conditions, technical indicators, allocation rules, news/events, and scoring.
3. **Decision support:** tax-lot context, paper trading, backtesting, and AI-generated explanations.
4. **Later:** broker integration, always behind explicit user confirmation and a separate execution boundary.

## Guardrails

- Use a licensed provider; do not scrape public finance websites.
- Clearly label delayed or end-of-day data.
- Make market-close, provider outage, stale-data, and duplicate-alert states visible.
- Keep trading credentials out of the MVP.
- Require explicit confirmation for any future broker order.
- Treat tax and investment output as informational, not professional advice.