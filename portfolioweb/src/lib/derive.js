import { scoreFactors, scoreFor } from './api.js'

function buildPositions(holdings, quotesBySymbol) {
  const enriched = holdings
    .filter((holding) => quotesBySymbol.has(holding.symbol))
    .map((holding) => {
      const quote = quotesBySymbol.get(holding.symbol)
      const marketValue = holding.quantity * quote.price
      const costBasis = holding.quantity * holding.averageCost
      const unrealizedGain = marketValue - costBasis
      const dayChange = quote.price - quote.previousClose

      return {
        ...holding,
        quote,
        marketValue,
        costBasis,
        unrealizedGain,
        unrealizedGainPercent: unrealizedGain / costBasis,
        dayChange,
        dayChangePercent: dayChange / quote.previousClose,
        dayGain: holding.quantity * dayChange,
      }
    })

  const totalMarketValue = enriched.reduce((sum, position) => sum + position.marketValue, 0)

  return enriched
    .map((position) => {
      const allocation = totalMarketValue ? position.marketValue / totalMarketValue : 0
      return {
        ...position,
        allocation,
        allocationDrift: allocation - position.targetAllocation,
      }
    })
    .sort((a, b) => b.marketValue - a.marketValue)
}

function summarize(positions) {
  const summary = positions.reduce(
    (totals, position) => {
      totals.marketValue += position.marketValue
      totals.costBasis += position.costBasis
      totals.unrealizedGain += position.unrealizedGain
      totals.dayGain += position.dayGain
      return totals
    },
    { marketValue: 0, costBasis: 0, unrealizedGain: 0, dayGain: 0 },
  )

  summary.unrealizedGainPercent = summary.costBasis ? summary.unrealizedGain / summary.costBasis : 0
  summary.previousValue = summary.marketValue - summary.dayGain
  summary.dayGainPercent = summary.previousValue ? summary.dayGain / summary.previousValue : 0
  return summary
}

const ARMED_BAND = 0.03

function ruleStateFor(entry, quote) {
  if (quote.price < entry.buyBelow || quote.price > entry.sellAbove) return 'TRIGGERED'

  const nearBuy = (quote.price - entry.buyBelow) / entry.buyBelow <= ARMED_BAND
  const nearSell = (entry.sellAbove - quote.price) / entry.sellAbove <= ARMED_BAND
  return nearBuy || nearSell ? 'ARMED' : 'NEUTRAL'
}

/**
 * Turn one API snapshot into everything the dashboard renders. Pure, so the same
 * snapshot always produces the same view.
 */
export function deriveDashboard(snapshot) {
  const quotesBySymbol = new Map(snapshot.quotes.map((quote) => [quote.symbol, quote]))

  const positions = buildPositions(snapshot.holdings, quotesBySymbol)
  const portfolioSummary = summarize(positions)

  const watchlistRows = snapshot.watchlist
    .filter((entry) => quotesBySymbol.has(entry.symbol))
    .map((entry) => {
      const quote = quotesBySymbol.get(entry.symbol)
      const dayChange = quote.price - quote.previousClose

      return {
        ...entry,
        quote,
        score: scoreFor(entry, quote),
        ruleState: ruleStateFor(entry, quote),
        dayChange,
        dayChangePercent: dayChange / quote.previousClose,
        distanceToBuy: (quote.price - entry.buyBelow) / entry.buyBelow,
        distanceToSell: (entry.sellAbove - quote.price) / entry.sellAbove,
      }
    })
    .sort((a, b) => b.score - a.score)

  const scoreBreakdowns = watchlistRows.map((row) => ({
    symbol: row.symbol,
    score: row.score,
    ruleState: row.ruleState,
    factors: scoreFactors(row, row.quote),
  }))

  const signalsById = new Map(snapshot.signals.map((signal) => [signal.id, signal]))
  const alerts = [...snapshot.alerts].sort((a, b) => new Date(b.sentAt) - new Date(a.sentAt))

  return {
    capturedAt: snapshot.capturedAt,
    marketStatus: snapshot.marketStatus,
    quotes: snapshot.quotes,
    positions,
    portfolioSummary,
    watchlistRows,
    scoreBreakdowns,
    signalsById,
    alerts,
    openSignals: snapshot.signals.filter((signal) => signal.status === 'OPEN'),
  }
}
