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
| 2 | Fetch quotes **in batches** | Partial | One request per symbol; `REALTIME_BULK_QUOTES` (premium, 100 symbols/request) unused |
| 3 | Slower-changing data (fundamentals, news, earnings, dividends) | Not started | No second timer, no `MarketEvent` |
| 4 | Technical indicators and allocation | Not started | No RSI/moving averages; allocation never computed |
| 5 | Evaluate versioned, deterministic rules | Not started | No `WatchRule`; nothing consumes the stored snapshots |
| 6 | Persist signals, suppress duplicates until reset | Not started | No `Signal`; quote-level dedup exists but is unrelated |
| 7 | Send email/push notifications | Not started | No `Alert`, no channel configured |
| 8 | Expose APIs used by the web UI | Partial | Only `POST /portfolio/import` and `GET /health`; no read endpoints |
| 9 | AI explanation of signals | Not started | Explicitly optional (Phase 3) |

Items 4-7 form the critical path: until they exist the app collects and stores data but never
produces a recommendation, which is the product's stated purpose.

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
| `Holding` | Done | Plus `Account`, `PortfolioImport`, `WatchlistSymbol` from CSV upload |
| `TaxLot` | Pending | Not in the positions export; needs a lot-level file or manual entry |
| `WatchRule` | Pending | Blocks the rule engine and every alert |
| `Signal` | Pending | Depends on `WatchRule` |
| `Alert` | Pending | Depends on `Signal`; no notification channel wired yet |
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

- **Dashboard:** portfolio value, allocation, open signals, and market status.
- **Watchlist:** symbol, price, change, rule state, and opportunity score.
- **Signal detail:** why it triggered, supporting facts, risks/events, and alert history.
- **Portfolio:** holdings, cost basis, tax lots, and allocation drift.
- **Rules and settings:** strategy conditions, notification channels, and provider status.

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