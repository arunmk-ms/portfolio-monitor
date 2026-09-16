import { headerKey, parseAmount, parseCsv, parsePercent } from './csv.js'

/**
 * Columns we deliberately never read. Account numbers are account-identifying
 * data the dashboard has no use for, so they are dropped at parse time and
 * never reach component state, the JSON export, or a future API payload.
 */
const IGNORED_COLUMNS = new Set(['accountnumber', 'accountnumbers'])

const COLUMN_ALIASES = {
  symbol: ['symbol', 'ticker'],
  description: ['description', 'securitydescription', 'name'],
  account: ['accountname', 'account'],
  quantity: ['quantity', 'shares', 'qty'],
  lastPrice: ['lastprice', 'price', 'currentprice'],
  currentValue: ['currentvalue', 'marketvalue', 'value'],
  costBasisTotal: ['costbasistotal', 'costbasis', 'totalcostbasis'],
  averageCost: ['averagecostbasis', 'averagecost', 'avgcostbasis'],
  totalGainLoss: ['totalgainlossdollar', 'totalgainloss'],
  totalGainLossPercent: ['totalgainlosspercent'],
  dayGainLoss: ['todaysgainlossdollar', 'daygainlossdollar'],
  dayGainLossPercent: ['todaysgainlosspercent', 'daygainlosspercent'],
  type: ['type', 'accounttype'],
}

const SYMBOL_PATTERN = /^[A-Za-z0-9][A-Za-z0-9.\-+]{0,11}$/

/** Money-market and sweep tickers are exported with a trailing "**". */
function isCashRow(symbol, description) {
  const upperSymbol = symbol.toUpperCase()
  const upperDescription = description.toUpperCase()
  return (
    upperSymbol.endsWith('**') ||
    upperSymbol === 'CASH' ||
    upperDescription.includes('HELD IN') ||
    upperDescription.includes('PENDING ACTIVITY') ||
    upperSymbol.includes('PENDING')
  )
}

/** Legal disclaimers and "Date downloaded ..." occupy only the first column. */
function isNoteRow(cells) {
  const filled = cells.filter((cell) => cell.trim() !== '')
  if (filled.length !== 1) return false
  const [only] = filled
  return only.trim().length > 40 || /^date downloaded/i.test(only.trim())
}

function isBlankRow(cells) {
  return cells.every((cell) => cell.trim() === '')
}

function buildColumnIndex(headerCells) {
  const normalized = headerCells.map(headerKey)
  const index = {}

  for (const [field, aliases] of Object.entries(COLUMN_ALIASES)) {
    const position = normalized.findIndex((key) => aliases.includes(key))
    if (position !== -1) index[field] = position
  }

  const ignored = headerCells.filter((_, i) => IGNORED_COLUMNS.has(normalized[i]))
  return { index, ignored, normalized }
}

/**
 * Parse a broker positions export into normalized holdings.
 *
 * Never throws on malformed content: every rejected row is reported as an issue
 * so the user can see exactly what was skipped and why.
 */
export function parsePositionsCsv(text, { sourceName = 'upload.csv' } = {}) {
  const rows = parseCsv(text)
  const issues = []

  const headerRowIndex = rows.findIndex(
    (cells) => cells.some((cell) => headerKey(cell) === 'symbol'),
  )

  if (headerRowIndex === -1) {
    return {
      ok: false,
      sourceName,
      positions: [],
      cash: [],
      issues: [
        {
          severity: 'error',
          message:
            'No "Symbol" column found. Export positions from your broker as CSV and upload that file unmodified.',
        },
      ],
      ignoredColumns: [],
      stats: { dataRows: 0, positions: 0, cash: 0, skipped: 0 },
    }
  }

  const { index, ignored } = buildColumnIndex(rows[headerRowIndex])

  const missing = ['symbol', 'quantity'].filter((field) => index[field] === undefined)
  if (missing.length) {
    return {
      ok: false,
      sourceName,
      positions: [],
      cash: [],
      issues: [
        {
          severity: 'error',
          message: `Missing required column(s): ${missing.join(', ')}.`,
        },
      ],
      ignoredColumns: ignored,
      stats: { dataRows: 0, positions: 0, cash: 0, skipped: 0 },
    }
  }

  const at = (cells, field) => {
    const position = index[field]
    return position === undefined ? '' : (cells[position] ?? '').trim()
  }

  const positions = []
  const cash = []
  const seen = new Map()
  let dataRows = 0
  let skipped = 0

  for (let i = headerRowIndex + 1; i < rows.length; i += 1) {
    const cells = rows[i]
    const line = i + 1

    if (isBlankRow(cells) || isNoteRow(cells)) continue

    dataRows += 1

    const symbol = at(cells, 'symbol')
    const description = at(cells, 'description')
    const account = at(cells, 'account')

    if (!symbol) {
      skipped += 1
      issues.push({ line, severity: 'warning', message: 'Row skipped: no symbol.' })
      continue
    }

    const currentValue = parseAmount(at(cells, 'currentValue'))

    if (isCashRow(symbol, description)) {
      cash.push({
        line,
        symbol: symbol.replace(/\*+$/, ''),
        description,
        account,
        currentValue,
      })
      continue
    }

    if (!SYMBOL_PATTERN.test(symbol)) {
      skipped += 1
      issues.push({
        line,
        severity: 'warning',
        message: `Row skipped: "${symbol}" is not a valid ticker.`,
      })
      continue
    }

    const quantity = parseAmount(at(cells, 'quantity'))
    const lastPrice = parseAmount(at(cells, 'lastPrice'))

    if (quantity === null) {
      skipped += 1
      issues.push({
        line,
        severity: 'warning',
        message: `${symbol} skipped: quantity is missing or unreadable.`,
      })
      continue
    }

    if (quantity <= 0) {
      skipped += 1
      issues.push({
        line,
        severity: 'warning',
        message: `${symbol} skipped: quantity is ${quantity}. Short positions are not supported yet.`,
      })
      continue
    }

    const costBasisTotal = parseAmount(at(cells, 'costBasisTotal'))
    let averageCost = parseAmount(at(cells, 'averageCost'))

    // Derive whichever cost figure the export omitted.
    if (averageCost === null && costBasisTotal !== null && quantity > 0) {
      averageCost = costBasisTotal / quantity
    }
    const resolvedCostBasis =
      costBasisTotal ?? (averageCost !== null ? averageCost * quantity : null)

    const symbolUpper = symbol.toUpperCase()
    const key = `${symbolUpper}|${account}`
    if (seen.has(key)) {
      issues.push({
        line,
        severity: 'warning',
        message: `${symbolUpper} appears more than once for the same account (also line ${seen.get(key)}). Both rows were kept.`,
      })
    } else {
      seen.set(key, line)
    }

    if (averageCost === null) {
      issues.push({
        line,
        severity: 'info',
        message: `${symbolUpper} has no cost basis. Unrealized gain cannot be calculated for it.`,
      })
    }

    // Cross-check the export's own arithmetic; a mismatch usually means the file
    // was hand-edited or the columns were mapped wrongly.
    if (quantity !== null && lastPrice !== null && currentValue !== null) {
      const expected = quantity * lastPrice
      const tolerance = Math.max(0.02, Math.abs(currentValue) * 0.01)
      if (Math.abs(expected - currentValue) > tolerance) {
        issues.push({
          line,
          severity: 'warning',
          message: `${symbolUpper}: quantity x price (${expected.toFixed(2)}) does not match the stated value (${currentValue.toFixed(2)}).`,
        })
      }
    }

    positions.push({
      line,
      symbol: symbolUpper,
      description,
      account,
      quantity,
      lastPrice,
      currentValue: currentValue ?? (lastPrice !== null ? quantity * lastPrice : null),
      costBasisTotal: resolvedCostBasis,
      averageCost,
      totalGainLoss: parseAmount(at(cells, 'totalGainLoss')),
      totalGainLossPercent: parsePercent(at(cells, 'totalGainLossPercent')),
      dayGainLoss: parseAmount(at(cells, 'dayGainLoss')),
      dayGainLossPercent: parsePercent(at(cells, 'dayGainLossPercent')),
    })
  }

  if (positions.length === 0 && cash.length === 0) {
    issues.unshift({
      severity: 'error',
      message: 'No positions found in this file.',
    })
  }

  return {
    ok: positions.length > 0,
    sourceName,
    positions,
    cash,
    issues,
    ignoredColumns: ignored,
    stats: { dataRows, positions: positions.length, cash: cash.length, skipped },
  }
}

/** Totals for the preview header. Cost basis is only summed where known. */
export function summarizeImport(result) {
  const marketValue = result.positions.reduce((sum, p) => sum + (p.currentValue ?? 0), 0)
  const costBasis = result.positions.reduce((sum, p) => sum + (p.costBasisTotal ?? 0), 0)
  const cashValue = result.cash.reduce((sum, c) => sum + (c.currentValue ?? 0), 0)
  const withoutCost = result.positions.filter((p) => p.costBasisTotal === null).length

  return {
    marketValue,
    costBasis,
    cashValue,
    totalValue: marketValue + cashValue,
    unrealizedGain: marketValue - costBasis,
    unrealizedGainPercent: costBasis ? (marketValue - costBasis) / costBasis : null,
    withoutCost,
  }
}

/**
 * The payload a future POST /api/holdings/import would send. Exposed so the
 * shape can be reviewed while the database design is still being settled.
 * Contains no account-identifying fields.
 */
export function toImportPayload(result) {
  return {
    source: 'csv-upload',
    sourceFile: result.sourceName,
    importedAt: new Date().toISOString(),
    holdings: result.positions.map((position) => ({
      symbol: position.symbol,
      description: position.description || null,
      account: position.account || null,
      quantity: position.quantity,
      averageCost: position.averageCost,
      costBasisTotal: position.costBasisTotal,
      lastPrice: position.lastPrice,
      currentValue: position.currentValue,
    })),
    cash: result.cash.map((entry) => ({
      symbol: entry.symbol,
      account: entry.account || null,
      value: entry.currentValue,
    })),
  }
}
