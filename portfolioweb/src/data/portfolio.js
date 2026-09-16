// Hardcoded seed data for the MVP UI.
// These shapes mirror the core entities in DESIGN.md and are consumed only
// through src/lib/api.js, which stands in for the portfolioapp Web API. The UI
// must not call a market-data provider directly.

export const marketStatus = {
  state: 'OPEN', // OPEN | CLOSED | PRE | POST
  dataQuality: 'DELAYED_15M', // REALTIME | DELAYED_15M | END_OF_DAY
  provider: 'sample-data (hardcoded)',
  capturedAt: '2026-09-02T19:35:00-07:00',
}

export const quotes = [
  {
    symbol: 'MSFT',
    name: 'Microsoft Corporation',
    price: 431.82,
    previousClose: 425.16,
    dayHigh: 434.5,
    dayLow: 424.9,
    fiftyTwoWeekHigh: 468.35,
    fiftyTwoWeekLow: 344.79,
    volume: 21450300,
    averageVolume: 18900000,
    rsi14: 58.4,
    sma50: 419.77,
    sma200: 402.13,
    capturedAt: '2026-09-02T19:35:00-07:00',
  },
  {
    symbol: 'NVDA',
    name: 'NVIDIA Corporation',
    price: 118.64,
    previousClose: 123.9,
    dayHigh: 124.31,
    dayLow: 117.82,
    fiftyTwoWeekHigh: 153.13,
    fiftyTwoWeekLow: 86.62,
    volume: 312880400,
    averageVolume: 245000000,
    rsi14: 28.7,
    sma50: 126.42,
    sma200: 118.05,
    capturedAt: '2026-09-02T19:35:00-07:00',
  },
]

export const holdings = [
  {
    id: 'h-msft-1',
    symbol: 'MSFT',
    quantity: 42,
    averageCost: 388.15,
    account: 'Brokerage',
    targetAllocation: 0.55,
  },
  {
    id: 'h-nvda-1',
    symbol: 'NVDA',
    quantity: 130,
    averageCost: 104.2,
    account: 'Brokerage',
    targetAllocation: 0.45,
  },
]

export const watchlist = [
  {
    symbol: 'MSFT',
    buyBelow: 400,
    sellAbove: 465,
    note: 'Core position. Trim only above the sell threshold.',
  },
  {
    symbol: 'NVDA',
    buyBelow: 120,
    sellAbove: 165,
    note: 'Adding on weakness below the buy threshold.',
  },
]

export const signals = [
  {
    id: 'sig-1042',
    symbol: 'NVDA',
    ruleId: 'price-below-buy-threshold',
    ruleVersion: 3,
    direction: 'BUY_CANDIDATE',
    score: 78,
    createdAt: '2026-09-02T19:32:00-07:00',
    status: 'OPEN',
    summary: 'NVDA traded below the $120.00 buy threshold.',
    facts: {
      price: 118.64,
      threshold: 120,
      rsi14: 28.7,
      sma50: 126.42,
      volumeRatio: 1.28,
    },
  },
  {
    id: 'sig-1041',
    symbol: 'NVDA',
    ruleId: 'allocation-above-limit',
    ruleVersion: 2,
    direction: 'REBALANCE',
    score: 55,
    createdAt: '2026-09-02T12:05:00-07:00',
    status: 'ACKNOWLEDGED',
    summary: 'NVDA allocation moved above its 45% target weight.',
    facts: {
      allocation: 0.4592,
      target: 0.45,
      drift: 0.0092,
    },
  },
  {
    id: 'sig-1038',
    symbol: 'MSFT',
    ruleId: 'earnings-within-days',
    ruleVersion: 1,
    direction: 'INFORMATIONAL',
    score: 20,
    createdAt: '2026-08-29T06:15:00-07:00',
    status: 'IGNORED',
    summary: 'MSFT earnings scheduled within 14 days.',
    facts: {
      eventType: 'EARNINGS',
      scheduledAt: '2026-09-10',
      daysUntil: 8,
    },
  },
]

export const alerts = [
  {
    id: 'alert-2201',
    signalId: 'sig-1042',
    symbol: 'NVDA',
    channel: 'EMAIL',
    sentAt: '2026-09-02T19:32:14-07:00',
    acknowledgedAt: null,
  },
  {
    id: 'alert-2200',
    signalId: 'sig-1041',
    symbol: 'NVDA',
    channel: 'PUSH',
    sentAt: '2026-09-02T12:05:40-07:00',
    acknowledgedAt: '2026-09-02T12:41:02-07:00',
  },
  {
    id: 'alert-2196',
    signalId: 'sig-1038',
    symbol: 'MSFT',
    channel: 'EMAIL',
    sentAt: '2026-08-29T06:15:09-07:00',
    acknowledgedAt: '2026-08-29T07:02:30-07:00',
  },
]
