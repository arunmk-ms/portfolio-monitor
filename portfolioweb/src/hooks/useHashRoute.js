import { useEffect, useState } from 'react'

export const ROUTES = {
  dashboard: { path: '#/dashboard', label: 'Dashboard' },
  import: { path: '#/import', label: 'Import' },
}

const DEFAULT_ROUTE = 'dashboard'

function routeFromHash() {
  const hash = window.location.hash
  const match = Object.entries(ROUTES).find(([, route]) => route.path === hash)
  return match ? match[0] : DEFAULT_ROUTE
}

/**
 * Minimal hash-based routing. Keeps deep links and browser back/forward working
 * without pulling in a router dependency for two pages.
 */
export function useHashRoute() {
  const [route, setRoute] = useState(routeFromHash)

  useEffect(() => {
    const sync = () => setRoute(routeFromHash())
    window.addEventListener('hashchange', sync)
    return () => window.removeEventListener('hashchange', sync)
  }, [])

  function navigate(next) {
    if (ROUTES[next]) window.location.hash = ROUTES[next].path
  }

  return { route, navigate }
}
