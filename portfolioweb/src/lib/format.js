const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
})

const compactCurrencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  notation: 'compact',
  maximumFractionDigits: 1,
})

const compactNumberFormatter = new Intl.NumberFormat('en-US', {
  notation: 'compact',
  maximumFractionDigits: 1,
})

export function formatCurrency(value) {
  return currencyFormatter.format(value)
}

export function formatCompactCurrency(value) {
  return compactCurrencyFormatter.format(value)
}

export function formatCompactNumber(value) {
  return compactNumberFormatter.format(value)
}

export function formatPercent(fraction, digits = 2) {
  return `${(fraction * 100).toFixed(digits)}%`
}

export function formatSignedCurrency(value) {
  const sign = value > 0 ? '+' : value < 0 ? '-' : ''
  return `${sign}${currencyFormatter.format(Math.abs(value))}`
}

export function formatSignedPercent(fraction, digits = 2) {
  const sign = fraction > 0 ? '+' : fraction < 0 ? '-' : ''
  return `${sign}${(Math.abs(fraction) * 100).toFixed(digits)}%`
}

export function formatDateTime(iso) {
  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

export function formatTimeOfDay(iso) {
  return new Date(iso).toLocaleTimeString('en-US', {
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function directionOf(value) {
  if (value > 0) return 'up'
  if (value < 0) return 'down'
  return 'flat'
}
