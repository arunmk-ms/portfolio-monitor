/**
 * Turns one API dashboard snapshot into everything the UI renders.
 *
 * Pure: the same snapshot always produces the same view. Every field the API can omit is treated
 * as genuinely absent rather than defaulted to zero — a position with no cost basis must read as
 * "unknown", not as a 100% gain.
 */

const asNumber = (value) => (typeof value === 'number' && Number.isFinite(value) ? value : null)

function buildPositions(snapshot, quotesBySymbol, allocationLimits) {
  const enriched = snapshot.holdings.map((holding) => {
    const quote = quotesBySymbol.get(holding.symbol) ?? null
    const quantity = asNumber(holding.quantity)
    const price = quote ? asNumber(quote.price) : null
    const averageCost = asNumber(holding.averageCost)
    const costBasis =
      asNumber(holding.costBasisTotal) ??
      (averageCost !== null && quantity !== null ? averageCost * quantity : null)

    // Prefer a live computation; fall back to the value the broker file carried so a position
    // still shows a value before the first monitoring run.
    const marketValue =
      quantity !== null && price !== null ? quantity * price : asNumber(holding.importedValue)

    const previousClose = quote ? asNumber(quote.previousClose) : null
    const dayChange = price !== null && previousClose !== null ? price - previousClose : null

    const unrealizedGain =
      marketValue !== null && costBasis !== null ? marketValue - costBasis : null

    return {
      id: `${holding.account ?? 'none'}:${holding.symbol}`,
      symbol: holding.symbol,
      description: holding.description ?? null,
      account: holding.account ?? null,
      positionType: holding.positionType,
      isMonitorable: holding.isMonitorable,
      quote,
      quantity,
      averageCost,
      costBasis,
      marketValue,
      unrealizedGain,
      unrealizedGainPercent:
        unrealizedGain !== null && costBasis ? unrealizedGain / costBasis : null,
      price,
      dayChange,
      dayChangePercent: dayChange !== null && previousClose ? dayChange / previousClose : null,
      dayGain: dayChange !== null && quantity !== null ? quantity * dayChange : null,
    }
  })

  const total = enriched.reduce((sum, p) => sum + (p.marketValue ?? 0), 0)

  return enriched
    .map((position) => {
      const allocation =
        total > 0 && position.marketValue !== null ? position.marketValue / total : null
      // Limits come from AllocationAbove rules; there is no separate "target weight" concept in
      // the schema, so nothing is invented when no such rule exists.
      const limit = allocationLimits.get(position.symbol) ?? null

      return {
        ...position,
        allocation,
        allocationLimit: limit,
        allocationOverLimit: allocation !== null && limit !== null ? allocation > limit : null,
      }
    })
    .sort((a, b) => (b.marketValue ?? 0) - (a.marketValue ?? 0))
}

function summarize(positions) {
  const summary = positions.reduce(
    (totals, position) => {
      totals.marketValue += position.marketValue ?? 0
      totals.costBasis += position.costBasis ?? 0
      totals.dayGain += position.dayGain ?? 0
      if (position.costBasis === null) totals.positionsWithoutCost += 1
      if (position.dayChange === null) totals.positionsWithoutDayChange += 1
      return totals
    },
    {
      marketValue: 0,
      costBasis: 0,
      dayGain: 0,
      positionsWithoutCost: 0,
      positionsWithoutDayChange: 0,
    },
  )

  summary.unrealizedGain = summary.costBasis ? summary.marketValue - summary.costBasis : null
  summary.unrealizedGainPercent = summary.costBasis
    ? (summary.marketValue - summary.costBasis) / summary.costBasis
    : null

  summary.previousValue = summary.marketValue - summary.dayGain
  // Day change is only meaningful once at least one position has a previous close to compare to.
  summary.dayGainPercent =
    summary.positionsWithoutDayChange === positions.length || !summary.previousValue
      ? null
      : summary.dayGain / summary.previousValue

  return summary
}

function buildWatchlistRows(snapshot, quotesBySymbol, signalsBySymbol) {
  return (snapshot.watchlist ?? [])
    .map((entry) => {
      const quote = quotesBySymbol.get(entry.symbol) ?? null
      const price = quote ? asNumber(quote.price) : null
      const previousClose = quote ? asNumber(quote.previousClose) : null
      const dayChange = price !== null && previousClose !== null ? price - previousClose : null

      const buyBelow = asNumber(entry.buyBelow)
      const sellAbove = asNumber(entry.sellAbove)

      // The score is the rule engine's, not the UI's: whichever open signal ranks highest.
      const open = (signalsBySymbol.get(entry.symbol) ?? []).filter((s) => s.status === 'Open')
      const score = open.length ? Math.max(...open.map((s) => asNumber(s.score) ?? 0)) : null

      return {
        ...entry,
        quote,
        price,
        score,
        openSignals: open,
        dayChange,
        dayChangePercent: dayChange !== null && previousClose ? dayChange / previousClose : null,
        distanceToBuy: price !== null && buyBelow ? (price - buyBelow) / buyBelow : null,
        distanceToSell: price !== null && sellAbove ? (sellAbove - price) / sellAbove : null,
      }
    })
    .sort((a, b) => (b.score ?? -1) - (a.score ?? -1) || a.symbol.localeCompare(b.symbol))
}

const FACT_LABELS = {
  price: 'Price',
  threshold: 'Threshold',
  previousClose: 'Previous close',
  rsi: 'RSI',
  rsi14: 'RSI (14)',
  movingAverage: 'Moving average',
  sma50: 'SMA 50',
  sma200: 'SMA 200',
  averageCost: 'Average cost',
  allocationPercent: 'Allocation %',
  percentChange: 'Change %',
  volumeRatio: 'Volume vs average',
  period: 'Period',
}

/**
 * Score breakdowns come from the facts the engine stored with each signal, so the explanation
 * always matches the evaluation that produced it rather than being recomputed here.
 */
function buildScoreBreakdowns(signals) {
  return signals
    .filter((signal) => signal.status === 'Open')
    .map((signal) => ({
      id: signal.id,
      symbol: signal.symbol,
      score: asNumber(signal.score) ?? 0,
      direction: signal.direction,
      ruleName: signal.ruleName,
      ruleVersion: signal.ruleVersion,
      createdAt: signal.createdAt,
      factors: Object.entries(signal.facts ?? {})
        .filter(([, value]) => typeof value === 'number' || typeof value === 'string')
        .map(([key, value]) => ({
          key,
          label: FACT_LABELS[key] ?? key,
          value,
        })),
    }))
    .sort((a, b) => b.score - a.score)
}

export function deriveDashboard(snapshot) {
  const quotes = snapshot.quotes ?? []
  const signals = snapshot.signals ?? []
  const alerts = snapshot.alerts ?? []

  const quotesBySymbol = new Map(quotes.map((quote) => [quote.symbol, quote]))
  const signalsBySymbol = new Map()
  for (const signal of signals) {
    const bucket = signalsBySymbol.get(signal.symbol)
    if (bucket) bucket.push(signal)
    else signalsBySymbol.set(signal.symbol, [signal])
  }

  // Thresholds are percentages in the rule store; the UI works in fractions.
  const allocationLimits = new Map()
  for (const entry of snapshot.watchlist ?? []) {
    const limit = (entry.rules ?? []).find(
      (rule) => rule.ruleType === 'AllocationAbove' && rule.isEnabled,
    )
    if (limit) allocationLimits.set(entry.symbol, asNumber(limit.threshold) / 100)
  }

  const positions = buildPositions(snapshot, quotesBySymbol, allocationLimits)

  return {
    capturedAt: snapshot.capturedAt,
    marketStatus: snapshot.marketStatus,
    lastImport: snapshot.lastImport ?? null,
    counts: snapshot.counts ?? {},
    quotes,
    positions,
    monitorable: positions.filter((p) => p.isMonitorable),
    cashPositions: positions.filter((p) => !p.isMonitorable),
    portfolioSummary: summarize(positions),
    watchlistRows: buildWatchlistRows(snapshot, quotesBySymbol, signalsBySymbol),
    scoreBreakdowns: buildScoreBreakdowns(signals),
    signals,
    signalsById: new Map(signals.map((signal) => [signal.id, signal])),
    openSignals: signals.filter((signal) => signal.status === 'Open'),
    alerts: [...alerts].sort((a, b) => new Date(b.sentAt ?? 0) - new Date(a.sentAt ?? 0)),
    // "Empty" means there is genuinely nothing to show. Signals can exist without holdings
    // (a rule can watch a symbol you don't own), and hiding those behind an import prompt
    // would bury real findings.
    isEmpty:
      positions.length === 0 &&
      (snapshot.watchlist ?? []).length === 0 &&
      signals.length === 0 &&
      alerts.length === 0,
  }
}
