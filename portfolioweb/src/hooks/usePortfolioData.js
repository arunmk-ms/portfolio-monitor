import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchSnapshot } from '../lib/api.js'

export const REFRESH_INTERVALS = [
  { label: '15s', ms: 15000 },
  { label: '30s', ms: 30000 },
  { label: '1m', ms: 60000 },
  { label: '5m', ms: 300000 },
]

const DEFAULT_INTERVAL_MS = 30000
const PREFS_KEY = 'portfolio-monitor.refresh'

function loadPrefs() {
  try {
    const raw = window.localStorage.getItem(PREFS_KEY)
    const parsed = raw ? JSON.parse(raw) : null
    if (!parsed) return null

    const known = REFRESH_INTERVALS.some((option) => option.ms === parsed.intervalMs)
    return {
      autoRefresh: Boolean(parsed.autoRefresh),
      intervalMs: known ? parsed.intervalMs : DEFAULT_INTERVAL_MS,
    }
  } catch {
    return null
  }
}

function savePrefs(prefs) {
  try {
    window.localStorage.setItem(PREFS_KEY, JSON.stringify(prefs))
  } catch {
    // Storage unavailable — preferences just will not persist.
  }
}

/**
 * Owns the dashboard snapshot plus its manual and automatic refresh behaviour.
 *
 * - A refresh already in flight is never duplicated.
 * - A failed refresh keeps the last good snapshot on screen and surfaces the error.
 * - Auto-refresh pauses while the tab is hidden and resumes on return.
 */
export function usePortfolioData({ enabled }) {
  const initialPrefs = useRef(null)
  if (initialPrefs.current === null) {
    initialPrefs.current = loadPrefs() ?? { autoRefresh: false, intervalMs: DEFAULT_INTERVAL_MS }
  }

  const [snapshot, setSnapshot] = useState(null)
  const [error, setError] = useState(null)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [lastUpdatedAt, setLastUpdatedAt] = useState(null)
  const [autoRefresh, setAutoRefreshState] = useState(initialPrefs.current.autoRefresh)
  const [intervalMs, setIntervalMsState] = useState(initialPrefs.current.intervalMs)
  const [secondsUntilRefresh, setSecondsUntilRefresh] = useState(null)
  const [isPaused, setIsPaused] = useState(false)

  const inFlight = useRef(false)
  const deadline = useRef(null)

  const refresh = useCallback(async () => {
    if (inFlight.current) return
    inFlight.current = true
    setIsRefreshing(true)

    try {
      const next = await fetchSnapshot()
      setSnapshot(next)
      setLastUpdatedAt(next.capturedAt)
      setError(null)
    } catch (cause) {
      // Keep the previous snapshot visible; a stale view beats an empty one.
      setError(cause.message ?? 'Refresh failed.')
    } finally {
      inFlight.current = false
      setIsRefreshing(false)
      deadline.current = Date.now() + intervalMs
    }
  }, [intervalMs])

  const setAutoRefresh = useCallback((value) => {
    setAutoRefreshState(value)
    setSecondsUntilRefresh(null)
  }, [])

  const setIntervalMs = useCallback((value) => {
    setIntervalMsState(value)
    deadline.current = Date.now() + value
  }, [])

  useEffect(() => {
    savePrefs({ autoRefresh, intervalMs })
  }, [autoRefresh, intervalMs])

  // First load once the user is authenticated; clear everything on sign-out.
  useEffect(() => {
    if (!enabled) {
      setSnapshot(null)
      setLastUpdatedAt(null)
      setError(null)
      return
    }
    refresh()
    // Deliberately keyed on `enabled` only: later refreshes are manual or come
    // from the timer below, and re-running this on every `refresh` identity
    // change would fetch each time the interval is edited.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled])

  // Pause the countdown while the tab is in the background.
  useEffect(() => {
    if (!enabled || !autoRefresh) {
      setIsPaused(false)
      return undefined
    }

    const sync = () => setIsPaused(document.visibilityState === 'hidden')
    sync()
    document.addEventListener('visibilitychange', sync)
    return () => document.removeEventListener('visibilitychange', sync)
  }, [enabled, autoRefresh])

  // A one-second ticker drives both the countdown and the refresh trigger, so
  // the number on screen is always the real time remaining.
  useEffect(() => {
    if (!enabled || !autoRefresh || isPaused) {
      setSecondsUntilRefresh(null)
      deadline.current = null
      return undefined
    }

    deadline.current = Date.now() + intervalMs
    setSecondsUntilRefresh(Math.round(intervalMs / 1000))

    const timer = setInterval(() => {
      if (inFlight.current || deadline.current === null) return

      const remaining = deadline.current - Date.now()
      if (remaining <= 0) {
        deadline.current = Date.now() + intervalMs
        setSecondsUntilRefresh(Math.round(intervalMs / 1000))
        refresh()
      } else {
        setSecondsUntilRefresh(Math.ceil(remaining / 1000))
      }
    }, 1000)

    return () => clearInterval(timer)
  }, [enabled, autoRefresh, isPaused, intervalMs, refresh])

  return {
    snapshot,
    error,
    isRefreshing,
    isInitialLoad: enabled && !snapshot && !error,
    lastUpdatedAt,
    autoRefresh,
    setAutoRefresh,
    intervalMs,
    setIntervalMs,
    secondsUntilRefresh,
    isPaused,
    refresh,
  }
}
