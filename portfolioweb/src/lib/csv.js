/**
 * Minimal RFC 4180 CSV reader.
 *
 * Broker exports quote any field containing a comma ("$3,567.50 ") and append
 * free-text legal footers, so a naive split(',') corrupts the data. This walks
 * the text character by character and tracks quote state instead.
 */
export function parseCsv(text) {
  if (typeof text !== 'string') return []

  // Excel-authored exports start with a UTF-8 byte order mark.
  const input = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text

  const rows = []
  let row = []
  let field = ''
  let inQuotes = false
  let hasContent = false

  const endField = () => {
    row.push(field)
    field = ''
  }

  const endRow = () => {
    endField()
    rows.push(row)
    row = []
    hasContent = false
  }

  for (let i = 0; i < input.length; i += 1) {
    const char = input[i]

    if (inQuotes) {
      if (char === '"') {
        if (input[i + 1] === '"') {
          field += '"'
          i += 1
        } else {
          inQuotes = false
        }
      } else {
        field += char
      }
      continue
    }

    if (char === '"') {
      inQuotes = true
      hasContent = true
    } else if (char === ',') {
      endField()
      hasContent = true
    } else if (char === '\n') {
      endRow()
    } else if (char !== '\r') {
      field += char
      hasContent = true
    }
  }

  if (hasContent || field.length > 0 || row.length > 0) endRow()

  return rows
}

/** Normalize a header cell so column renames/reordering do not break lookups. */
export function headerKey(value) {
  return String(value ?? '')
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '')
}

/**
 * Parse a broker-formatted amount.
 * Handles "$6.48 ", "$3,567.50 ", "($39,573.87)", "-1.37%", "--", "" and "n/a".
 * Returns null when there is no usable number.
 */
export function parseAmount(value) {
  if (value === null || value === undefined) return null

  let text = String(value).trim()
  if (!text || text === '--' || text === '-' || /^n\/?a$/i.test(text)) return null

  // Accounting notation: (1,234.56) means negative.
  let negative = false
  if (text.startsWith('(') && text.endsWith(')')) {
    negative = true
    text = text.slice(1, -1)
  }

  text = text.replace(/[$,\s%]/g, '')
  if (text.startsWith('-')) {
    negative = true
    text = text.slice(1)
  }
  if (text.startsWith('+')) text = text.slice(1)

  if (!text || !/^\d*\.?\d+$/.test(text)) return null

  const parsed = Number(text)
  if (!Number.isFinite(parsed)) return null

  return negative ? -parsed : parsed
}

/** Parse a percentage cell into a fraction: "-1.37%" -> -0.0137. */
export function parsePercent(value) {
  const parsed = parseAmount(value)
  return parsed === null ? null : parsed / 100
}
