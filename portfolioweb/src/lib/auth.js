// MOCK AUTHENTICATION — replace before this app leaves local development.
//
// There is no backend yet, so credentials are checked in the browser against a
// hardcoded list. That is fine for a local UI prototype and unsafe for anything
// else: anyone can read this file. When portfolioapp exposes a sign-in endpoint,
// swap signIn() for a real request and keep the rest of the module unchanged.
//
// These are throwaway demo credentials for a local prototype, not secrets.

const DEMO_USERS = [
  {
    username: 'demo',
    password: 'portfolio123',
    displayName: 'Demo Investor',
    email: 'demo@example.com',
  },
]

const STORAGE_KEY = 'portfolio-monitor.session'
const SESSION_TTL_MS = 8 * 60 * 60 * 1000

function storageFor(remember) {
  return remember ? window.localStorage : window.sessionStorage
}

function safeRead(storage) {
  try {
    const raw = storage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

function safeWrite(storage, session) {
  try {
    storage.setItem(STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Storage can be unavailable (private mode, blocked cookies). The session
    // still works for the current page, it just will not survive a reload.
  }
}

function safeClear() {
  try {
    window.localStorage.removeItem(STORAGE_KEY)
    window.sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // Ignore — nothing to clean up if storage is unavailable.
  }
}

function toSession(user, remember) {
  return {
    // Stand-in for a bearer token from the API. Never store a password.
    token: `mock-${btoa(`${user.username}:${Date.now()}`)}`,
    username: user.username,
    displayName: user.displayName,
    email: user.email,
    remember,
    issuedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + SESSION_TTL_MS).toISOString(),
  }
}

export function isExpired(session) {
  return !session?.expiresAt || Date.parse(session.expiresAt) <= Date.now()
}

export function restoreSession() {
  const session = safeRead(window.localStorage) ?? safeRead(window.sessionStorage)
  if (!session) return null

  if (isExpired(session)) {
    safeClear()
    return null
  }

  return session
}

export async function signIn({ username, password, remember = true }) {
  // Simulated network latency so the loading state is exercised.
  await new Promise((resolve) => setTimeout(resolve, 450))

  const candidate = String(username ?? '').trim().toLowerCase()
  const user = DEMO_USERS.find((entry) => entry.username === candidate)

  // Compare both fields regardless of which one failed so the error message
  // cannot be used to discover valid usernames.
  if (!user || user.password !== password) {
    throw new Error('Incorrect username or password.')
  }

  const session = toSession(user, remember)
  safeClear()
  safeWrite(storageFor(remember), session)
  return session
}

export function signOut() {
  safeClear()
}

export const demoCredentials = {
  username: DEMO_USERS[0].username,
  password: DEMO_USERS[0].password,
}
