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

  const headers = {}
  if (FUNCTION_KEY) headers['x-functions-key'] = FUNCTION_KEY

  let response
  try {
    response = await fetch(`${API_BASE}/portfolio/import`, {
      method: 'POST',
      body: form,
      headers,
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
