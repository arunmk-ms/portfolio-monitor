import { useRef, useState } from 'react'
import { demoCredentials } from '../lib/auth.js'

export default function LoginScreen({ onSignIn }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  // State updates are batched, so several submits fired in one tick would all
  // pass an isSubmitting check. A ref rejects them synchronously.
  const inFlight = useRef(false)

  async function handleSubmit(event) {
    event.preventDefault()
    if (inFlight.current) return

    inFlight.current = true
    setError(null)
    setIsSubmitting(true)

    try {
      await onSignIn({ username, password, remember })
    } catch (cause) {
      setError(cause.message ?? 'Sign in failed.')
      setPassword('')
      setIsSubmitting(false)
      inFlight.current = false
    }
    // On success the dashboard replaces this screen, so there is nothing to reset.
  }

  function fillDemo() {
    setUsername(demoCredentials.username)
    setPassword(demoCredentials.password)
    setError(null)
  }

  return (
    <div className="login">
      <div className="login__card">
        <div className="login__brand">
          <span className="login__mark" aria-hidden="true">
            ◪
          </span>
          <div>
            <p className="header__eyebrow">Portfolio Monitor</p>
            <h1 className="login__title">Sign in</h1>
          </div>
        </div>

        <p className="login__lede">
          Holdings, watchlist, and alert history for your account.
        </p>

        <form className="login__form" onSubmit={handleSubmit} noValidate>
          <label className="field">
            <span className="field__label">Username</span>
            <input
              className="field__input"
              type="text"
              name="username"
              autoComplete="username"
              autoFocus
              required
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              disabled={isSubmitting}
            />
          </label>

          <label className="field">
            <span className="field__label">Password</span>
            <span className="field__control">
              <input
                className="field__input"
                type={showPassword ? 'text' : 'password'}
                name="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                disabled={isSubmitting}
              />
              <button
                type="button"
                className="field__toggle"
                onClick={() => setShowPassword((value) => !value)}
                aria-pressed={showPassword}
                disabled={isSubmitting}
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </span>
          </label>

          <label className="checkbox">
            <input
              type="checkbox"
              checked={remember}
              onChange={(event) => setRemember(event.target.checked)}
              disabled={isSubmitting}
            />
            <span>Keep me signed in on this device</span>
          </label>

          <p className="login__error" role="alert" aria-live="polite">
            {error}
          </p>

          <button className="button button--primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>

        <div className="login__demo">
          <p className="subtle">
            Prototype build with mock authentication. Use{' '}
            <code>{demoCredentials.username}</code> / <code>{demoCredentials.password}</code>.
          </p>
          <button type="button" className="button button--ghost" onClick={fillDemo} disabled={isSubmitting}>
            Use demo credentials
          </button>
        </div>
      </div>
    </div>
  )
}
