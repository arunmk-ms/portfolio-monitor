import { formatTimeOfDay } from '../lib/format.js'

const stateLabels = {
  OPEN: 'Market open',
  CLOSED: 'Market closed',
}

const closedReasons = {
  Weekend: 'weekend',
  Holiday: 'holiday',
  BeforeOpen: 'before open',
  AfterClose: 'after close',
}

/**
 * Data-quality wording is deliberately explicit. DESIGN.md requires delayed, end-of-day, and
 * stale data to be visible rather than presented as live.
 */
const qualityLabels = {
  REALTIME: 'Real-time data',
  DELAYED_15M: 'Delayed data',
  END_OF_DAY: 'End-of-day data',
  IMPORTED: 'Prices from import',
  NONE: 'No price data',
}

export default function MarketStatus({ status, lastUpdatedAt }) {
  const state = status.state === 'OPEN' ? 'OPEN' : 'CLOSED'
  const reason = state === 'CLOSED' ? closedReasons[status.reason] : null

  return (
    <div className="header__meta">
      <span className={`pill pill--${state === 'OPEN' ? 'live' : 'muted'}`}>
        <span className="pill__dot" />
        {stateLabels[state]}
        {reason ? ` · ${reason}` : ''}
      </span>
      <span className={`pill ${status.dataQuality === 'NONE' ? 'pill--danger' : 'pill--warn'}`}>
        {qualityLabels[status.dataQuality] ?? status.dataQuality}
      </span>
      <span className="header__timestamp">
        {lastUpdatedAt ? `Updated ${formatTimeOfDay(lastUpdatedAt)}` : 'Loading…'} ·{' '}
        {status.provider}
      </span>
    </div>
  )
}
