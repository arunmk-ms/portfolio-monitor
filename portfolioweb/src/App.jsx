import { useCallback, useMemo, useState } from 'react'
import './App.css'
import Header from './components/Header.jsx'
import LoginScreen from './components/LoginScreen.jsx'
import MarketStatus from './components/MarketStatus.jsx'
import RefreshControls from './components/RefreshControls.jsx'
import ImportPage from './components/ImportPage.jsx'
import SummaryCards from './components/SummaryCards.jsx'
import PriceBoard from './components/PriceBoard.jsx'
import HoldingsTable from './components/HoldingsTable.jsx'
import AllocationPanel from './components/AllocationPanel.jsx'
import WatchlistPanel from './components/WatchlistPanel.jsx'
import ScorePanel from './components/ScorePanel.jsx'
import AlertHistory from './components/AlertHistory.jsx'
import EmptyState from './components/EmptyState.jsx'
import { useSession } from './hooks/useSession.js'
import { usePortfolioData } from './hooks/usePortfolioData.js'
import { useHashRoute } from './hooks/useHashRoute.js'
import { deriveDashboard } from './lib/derive.js'
import { updateSignalStatus } from './lib/portfolioApi.js'

const FALLBACK_STATUS = { state: 'CLOSED', dataQuality: 'NONE', provider: 'not connected' }

export default function App() {
  const { session, isAuthenticated, login, logout } = useSession()
  const { route } = useHashRoute()
  const isDashboard = route === 'dashboard'

  // Only poll on the dashboard; the import page has no live data.
  const data = usePortfolioData({ enabled: isAuthenticated && isDashboard })
  const [reviewingId, setReviewingId] = useState(null)
  const [reviewError, setReviewError] = useState(null)

  const view = useMemo(
    () => (data.snapshot ? deriveDashboard(data.snapshot) : null),
    [data.snapshot],
  )

  const { refresh } = data
  const handleReview = useCallback(
    async (signalId, status) => {
      setReviewingId(signalId)
      setReviewError(null)
      try {
        await updateSignalStatus(signalId, status)
        // Re-read rather than patching locally: the server also acknowledges the linked alerts,
        // so a local edit would leave the alert history disagreeing with the signal.
        await refresh()
      } catch (cause) {
        setReviewError(cause.message ?? 'Could not update the signal.')
      } finally {
        setReviewingId(null)
      }
    },
    [refresh],
  )

  if (!isAuthenticated) {
    return <LoginScreen onSignIn={login} />
  }

  return (
    <div className="app">
      <Header
        user={session}
        onSignOut={logout}
        route={route}
        title={isDashboard ? 'Dashboard' : 'Import'}
      >
        {isDashboard ? (
          <>
            <MarketStatus
              status={view?.marketStatus ?? FALLBACK_STATUS}
              lastUpdatedAt={data.lastUpdatedAt}
            />
            <RefreshControls
              onRefresh={data.refresh}
              isRefreshing={data.isRefreshing}
              autoRefresh={data.autoRefresh}
              onAutoRefreshChange={data.setAutoRefresh}
              intervalMs={data.intervalMs}
              onIntervalChange={data.setIntervalMs}
              secondsUntilRefresh={data.secondsUntilRefresh}
              isPaused={data.isPaused}
            />
          </>
        ) : null}
      </Header>

      {isDashboard && data.error ? (
        <div className="banner banner--error" role="alert">
          <span>{data.error}</span>
          <button type="button" className="button button--ghost" onClick={data.refresh}>
            Try again
          </button>
        </div>
      ) : null}

      {isDashboard && reviewError ? (
        <div className="banner banner--error" role="alert">
          <span>{reviewError}</span>
        </div>
      ) : null}

      {!isDashboard ? (
        <ImportPage />
      ) : view ? (
        <main className={`app__main ${data.isRefreshing ? 'app__main--refreshing' : ''}`}>
          {view.isEmpty ? (
            <section className="panel">
              <EmptyState
                title="Your portfolio is empty"
                action={
                  <a className="button button--primary" href="#/import">
                    Import a positions CSV
                  </a>
                }
              >
                Upload a broker positions export to populate holdings, the watchlist, and
                everything derived from them.
              </EmptyState>
            </section>
          ) : (
            <>
              <SummaryCards
                summary={view.portfolioSummary}
                openSignalCount={view.openSignals.length}
                positionCount={view.positions.length}
                counts={view.counts}
              />

              <PriceBoard quotes={view.quotes} />

              <div className="grid grid--two">
                <HoldingsTable positions={view.positions} totals={view.portfolioSummary} />
                <AllocationPanel
                  positions={view.positions}
                  totalValue={view.portfolioSummary.marketValue}
                />
              </div>

              <div className="grid grid--two">
                <WatchlistPanel rows={view.watchlistRows} />
                <ScorePanel
                  breakdowns={view.scoreBreakdowns}
                  onReview={handleReview}
                  pendingId={reviewingId}
                  hasRules={Boolean(view.counts?.enabledRules)}
                />
              </div>

              <AlertHistory
                alerts={view.alerts}
                signalsById={view.signalsById}
                hasSignals={view.signals.length > 0}
              />
            </>
          )}
        </main>
      ) : (
        <div className="boot" role="status">
          Loading portfolio…
        </div>
      )}

      <footer className="app__footer">
        {view?.lastImport
          ? `Holdings from ${view.lastImport.fileName}. `
          : ''}
        Prototype build with mock authentication. Signals are recommendations for review, not
        orders.
      </footer>
    </div>
  )
}
