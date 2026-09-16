// MOCK MARKET-DATA API — stands in for the portfolioapp Web API.
//
// The UI must never call a market-data provider directly (see DESIGN.md), so all
// reads go through this module. It keeps the request shape the real API will
// have — an async call returning one timestamped snapshot — so swapping in
// `fetch('/api/snapshot')` later touches only this file.
//
// Between calls it applies a small random walk to the seed quotes and runs the
// threshold rules, so a manual or automatic refresh produces visibly new data.

import {
  alerts as seedAlerts,
  holdings,
  marketStatus,
  quotes as seedQuotes,
  signals as seedSignals,
  watchlist,
} from '../data/portfolio.js'

const clamp = (value, min, max) => Math.min(max, Math.max(min, value))
const round = (value, digits = 2) => Number(value.toFixed(digits))

// Mutable live state, seeded once per page load.
let liveQuotes = seedQuotes.map((quote) => ({ ...quote }))
let liveSignals = seedSignals.map((signal) => ({ ...signal, facts: { ...signal.facts } }))
let liveAlerts = seedAlerts.map((alert) => ({ ...alert }))
let signalSequence = 1042
let alertSequence = 2201
let isFirstFetch = true

function tickQuote(quote) {
  // Volatility roughly proportional to price, biased slightly toward the
  // 50-day average so the walk stays in a believable range.
  const drift = (quote.sma50 - quote.price) * 0.02
  const shock = (Math.random() - 0.5) * quote.price * 0.006
  const price = clamp(
    quote.price + drift + shock,
    quote.fiftyTwoWeekLow,
    quote.fiftyTwoWeekHigh,
  )

  const priceChange = price - quote.price
  // RSI nudges in the direction of the move and mean-reverts toward 50.
  const rsi14 = clamp(quote.rsi14 + (priceChange / quote.price) * 900 + (50 - quote.rsi14) * 0.03, 1, 99)

  return {
    ...quote,
    price: round(price),
    dayHigh: round(Math.max(quote.dayHigh, price)),
    dayLow: round(Math.min(quote.dayLow, price)),
    volume: Math.round(quote.volume + quote.averageVolume * (0.002 + Math.random() * 0.004)),
    rsi14: round(rsi14, 1),
  }
}

function evaluateRules(quotesBySymbol, capturedAt) {
  const newSignals = []
  const newAlerts = []

  for (const entry of watchlist) {
    const quote = quotesBySymbol.get(entry.symbol)
    if (!quote) continue

    const checks = [
      {
        ruleId: 'price-below-buy-threshold',
        ruleVersion: 3,
        direction: 'BUY_CANDIDATE',
        active: quote.price < entry.buyBelow,
        threshold: entry.buyBelow,
        summary: `${entry.symbol} traded below the $${entry.buyBelow.toFixed(2)} buy threshold.`,
      },
      {
        ruleId: 'price-above-sell-threshold',
        ruleVersion: 2,
        direction: 'SELL_CANDIDATE',
        active: quote.price > entry.sellAbove,
        threshold: entry.sellAbove,
        summary: `${entry.symbol} traded above the $${entry.sellAbove.toFixed(2)} sell threshold.`,
      },
    ]

    for (const check of checks) {
      const existing = liveSignals.find(
        (signal) =>
          signal.symbol === entry.symbol &&
          signal.ruleId === check.ruleId &&
          signal.status !== 'RESET',
      )

      if (check.active) {
        // Deduplicate: only raise a new signal when the condition was not
        // already standing (DESIGN.md — suppress duplicates until it resets).
        if (existing) continue

        const signal = {
          id: `sig-${++signalSequence}`,
          symbol: entry.symbol,
          ruleId: check.ruleId,
          ruleVersion: check.ruleVersion,
          direction: check.direction,
          score: scoreFor(entry, quote),
          createdAt: capturedAt,
          status: 'OPEN',
          summary: check.summary,
          facts: {
            price: quote.price,
            threshold: check.threshold,
            rsi14: quote.rsi14,
            sma50: quote.sma50,
            volumeRatio: round(quote.volume / quote.averageVolume),
          },
        }

        newSignals.push(signal)
        newAlerts.push({
          id: `alert-${++alertSequence}`,
          signalId: signal.id,
          symbol: entry.symbol,
          channel: 'EMAIL',
          sentAt: capturedAt,
          acknowledgedAt: null,
        })
      } else if (existing) {
        // Condition cleared — reset it so a future crossing alerts again.
        existing.status = 'RESET'
        existing.resetAt = capturedAt
      }
    }
  }

  liveSignals = [...newSignals, ...liveSignals]
  liveAlerts = [...newAlerts, ...liveAlerts]

  return { newSignals, newAlerts }
}

const SCORE_WEIGHTS = { value: 0.35, momentum: 0.25, trend: 0.2, volume: 0.2 }

export function scoreFactors(entry, quote) {
  return [
    {
      key: 'value',
      label: 'Price vs buy threshold',
      weight: clamp(((entry.buyBelow - quote.price) / entry.buyBelow) * 100 + 50, 0, 100),
      detail: `${entry.symbol} at ${quote.price.toFixed(2)} vs buy below ${entry.buyBelow.toFixed(2)}`,
    },
    {
      key: 'momentum',
      label: 'Momentum (RSI 14)',
      weight: clamp(100 - quote.rsi14, 0, 100),
      detail: `RSI ${quote.rsi14.toFixed(1)} — ${
        quote.rsi14 < 30 ? 'oversold' : quote.rsi14 > 70 ? 'overbought' : 'neutral'
      }`,
    },
    {
      key: 'trend',
      label: 'Trend vs 50-day average',
      weight: clamp(((quote.price - quote.sma50) / quote.sma50) * 400 + 50, 0, 100),
      detail: `${quote.price >= quote.sma50 ? 'Above' : 'Below'} SMA50 ${quote.sma50.toFixed(2)}`,
    },
    {
      key: 'volume',
      label: 'Volume vs average',
      weight: clamp((quote.volume / quote.averageVolume) * 50, 0, 100),
      detail: `${(quote.volume / quote.averageVolume).toFixed(2)}x average volume`,
    },
  ]
}

export function scoreFor(entry, quote) {
  const total = scoreFactors(entry, quote).reduce(
    (sum, factor) => sum + factor.weight * SCORE_WEIGHTS[factor.key],
    0,
  )
  return Math.round(total)
}

/**
 * Fetch one dashboard snapshot. Mirrors an async API call, including latency
 * and the possibility of a provider failure the UI has to surface.
 */
export async function fetchSnapshot({ simulateFailure = false } = {}) {
  await new Promise((resolve) => setTimeout(resolve, 300 + Math.random() * 350))

  if (simulateFailure) {
    throw new Error('Market-data provider did not respond. Showing the last good snapshot.')
  }

  // Keep the seeded values on the very first load so the dashboard opens with
  // the documented sample numbers; move the market on every refresh after that.
  if (isFirstFetch) {
    isFirstFetch = false
  } else {
    liveQuotes = liveQuotes.map(tickQuote)
  }

  const capturedAt = new Date().toISOString()
  const quotesBySymbol = new Map(liveQuotes.map((quote) => [quote.symbol, quote]))
  const { newSignals } = evaluateRules(quotesBySymbol, capturedAt)

  return {
    capturedAt,
    marketStatus: { ...marketStatus, capturedAt },
    quotes: liveQuotes.map((quote) => ({ ...quote })),
    holdings,
    watchlist,
    signals: liveSignals.map((signal) => ({ ...signal })),
    alerts: liveAlerts.map((alert) => ({ ...alert })),
    newSignalCount: newSignals.length,
  }
}
