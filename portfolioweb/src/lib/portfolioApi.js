/**
 * Client for the portfolioapp Functions API.
 *
 * The base URL defaults to a relative "/api", which the Vite dev server proxies
 * to the local Functions host (see vite.config.js). Set VITE_API_BASE_URL to
 * point a build at a deployed backend.
 */
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/+$/, '')
const FUNCTION_KEY = import.meta.env.VITE_API_FUNCTION_KEY ?? ''

export class ApiError extends Error {
  constructor(message, { status = 0, cause } = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.cause = cause
  }
}

async function readBody(response) {
  const text = await response.text().catch(() => '')
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    // The Functions host returns plain text for some failures (e.g. a missing key).
    // Ignore HTML error pages from proxies so raw markup never reaches the UI.
    const trimmed = text.trim()
    if (trimmed.startsWith('<')) return null
    return { error: trimmed.slice(0, 300) }
  }
}

function messageForStatus(status, body) {
  if (body?.error) return body.error

  switch (status) {
    case 401:
    case 403:
      return 'The portfolio API rejected the request. Check VITE_API_FUNCTION_KEY.'
    case 404:
      return 'The import endpoint was not found. Is the portfolioapp Functions host running?'
    case 413:
      return 'The file is too large for the portfolio API.'
    case 502:
    case 503:
    case 504:
      // A gateway error almost always means the dev proxy could not reach the
      // Functions host, rather than the host itself failing.
      return 'The portfolio API is not reachable. Start the portfolioapp Functions host and try again.'
    default:
      return status >= 500
        ? `The portfolio API failed (HTTP ${status}). Check the Functions host logs.`
        : `The portfolio API returned HTTP ${status}.`
  }
}

/**
 * POST a positions export to /portfolio/import.
 *
 * The backend parses the CSV itself, so the original file is sent unmodified
 * rather than the browser's parsed preview — the server stays the source of
 * truth for what gets stored.
 *
 * @returns {Promise<{importId: number, positionsImported: number, cashRowsSkipped: number, symbolsAddedToWatchlist: number}>}
 */
export async function uploadPositionsCsv(file, { signal } = {}) {
  const form = new FormData()
  form.append('file', file, file.name)

  let response
  try {
    response = await fetch(`${API_BASE}/portfolio/import`, {
      method: 'POST',
      body: form,
      headers: authHeaders(),
      signal,
    })
  } catch (cause) {
    if (cause.name === 'AbortError') throw cause
    throw new ApiError(
      'Could not reach the portfolio API. Start the portfolioapp Functions host and try again.',
      { cause },
    )
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw new ApiError(messageForStatus(response.status, body), { status: response.status })
  }

  if (!body || typeof body.importId !== 'number') {
    throw new ApiError('The portfolio API returned an unexpected response.', {
      status: response.status,
    })
  }

  return body
}

export const apiConfig = { baseUrl: API_BASE, hasFunctionKey: Boolean(FUNCTION_KEY) }

function authHeaders() {
  return FUNCTION_KEY ? { 'x-functions-key': FUNCTION_KEY } : {}
}

async function getJson(path, { signal } = {}) {
  let response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: authHeaders(), signal })
  } catch (cause) {
    if (cause.name === 'AbortError') throw cause
    throw new ApiError(
      'Could not reach the portfolio API. Start the portfolioapp Functions host and try again.',
      { cause },
    )
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw new ApiError(messageForStatus(response.status, body), { status: response.status })
  }

  return body
}

/**
 * Fetch the composed dashboard snapshot.
 *
 * One request rather than several so every panel renders from the same instant; separate calls
 * would let totals and allocation disagree with the prices shown beside them.
 */
export async function fetchDashboard({ signal } = {}) {
  const body = await getJson('/dashboard', { signal })

  if (!body || !Array.isArray(body.holdings)) {
    throw new ApiError('The portfolio API returned an unexpected dashboard response.')
  }

  return body
}

/**
 * Acknowledge or ignore a signal — the review actions DESIGN.md calls for.
 *
 * @param {number} id Signal id.
 * @param {'Acknowledged'|'Ignored'} status
 */
export async function updateSignalStatus(id, status, { signal } = {}) {
  let response
  try {
    response = await fetch(
      `${API_BASE}/signals/${id}/status?status=${encodeURIComponent(status)}`,
      { method: 'POST', headers: authHeaders(), signal },
    )
  } catch (cause) {
    if (cause.name === 'AbortError') throw cause
    throw new ApiError('Could not reach the portfolio API.', { cause })
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw new ApiError(messageForStatus(response.status, body), { status: response.status })
  }

  return body
}

async function sendJson(path, method, payload, { signal } = {}) {
  let response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: payload === undefined ? undefined : JSON.stringify(payload),
      signal,
    })
  } catch (cause) {
    if (cause.name === 'AbortError') throw cause
    throw new ApiError(
      'Could not reach the portfolio API. Start the portfolioapp Functions host and try again.',
      { cause },
    )
  }

  const body = await readBody(response)

  if (!response.ok) {
    throw new ApiError(messageForStatus(response.status, body), { status: response.status })
  }

  return body
}

/**
 * Add a position by hand, or update the shares on one that already exists.
 *
 * The server owns validation so the rules cannot drift between the form and the API; a rejected
 * entry comes back as a plain-language message the form shows directly.
 */
export function createHolding({ symbol, quantity, averageCost, account, description }, options) {
  return sendJson('/holdings', 'POST', { symbol, quantity, averageCost, account, description }, options)
}

/** Remove a position, so a mistyped entry can be corrected. */
export function deleteHolding(id, options) {
  return sendJson(`/holdings/${id}`, 'DELETE', undefined, options)
}

/** Positions with enough detail to manage them, including whether each was imported or typed in. */
export async function fetchManageableHoldings(options) {
  const body = await getJson('/holdings/manage', options)
  return Array.isArray(body) ? body : []
}
