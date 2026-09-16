import { useCallback, useEffect, useState } from 'react'
import { isExpired, restoreSession, signIn, signOut } from '../lib/auth.js'

export function useSession() {
  // Read storage during initialization so a returning user never sees the login
  // screen flash before the saved session is restored.
  const [session, setSession] = useState(restoreSession)

  const login = useCallback(async (credentials) => {
    const next = await signIn(credentials)
    setSession(next)
    return next
  }, [])

  const logout = useCallback(() => {
    signOut()
    setSession(null)
  }, [])

  // Drop the session the moment it expires rather than waiting for a reload.
  useEffect(() => {
    if (!session) return undefined

    const msLeft = Math.max(0, Date.parse(session.expiresAt) - Date.now())
    const timer = setTimeout(logout, msLeft)
    return () => clearTimeout(timer)
  }, [session, logout])

  return {
    session,
    isAuthenticated: Boolean(session) && !isExpired(session),
    login,
    logout,
  }
}
