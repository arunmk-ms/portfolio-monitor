import { useMemo } from 'react'
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
import { useSession } from './hooks/useSession.js'
import { usePortfolioData } from './hooks/usePortfolioData.js'
import { useHashRoute } from './hooks/useHashRoute.js'
import { deriveDashboard } from './lib/derive.js'

const FALLBACK_STATUS = { state: 'CLOSED', dataQuality: 'DELAYED_15M', provider: '—' }

export default function App() {
  const { session, isAuthenticated, login, logout } = useSession()
  const { route } = useHashRoute()
  const isDashboard = route === 'dashboard'

  // Only poll on the dashboard; the import page has no live data.
  const data = usePortfolioData({ enabled: isAuthenticated && isDashboard })

  const view = useMemo(
    () => (data.snapshot ? deriveDashboard(data.snapshot) : null),
    [data.snapshot],
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

      {!isDashboard ? (
        <ImportPage />
      ) : view ? (
        <main className={`app__main ${data.isRefreshing ? 'app__main--refreshing' : ''}`}>
          <SummaryCards
            summary={view.portfolioSummary}
            openSignalCount={view.openSignals.length}
            positionCount={view.positions.length}
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
            <ScorePanel breakdowns={view.scoreBreakdowns} />
          </div>

          <AlertHistory alerts={view.alerts} signalsById={view.signalsById} />
        </main>
      ) : (
        <div className="boot" role="status">
          Loading portfolio…
        </div>
      )}

      <footer className="app__footer">
        Prototype build with mock authentication and simulated market data. Signals are
        recommendations for review, not orders.
      </footer>
    </div>
  )
}
