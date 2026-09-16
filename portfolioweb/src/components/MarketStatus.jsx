import { formatTimeOfDay } from '../lib/format.js'

const stateLabels = {
  OPEN: 'Market open',
  CLOSED: 'Market closed',
  PRE: 'Pre-market',
  POST: 'After hours',
}

const qualityLabels = {
  REALTIME: 'Real-time data',
  DELAYED_15M: 'Delayed 15 min',
  END_OF_DAY: 'End-of-day data',
}

export default function MarketStatus({ status, lastUpdatedAt }) {
  return (
    <div className="header__meta">
      <span className={`pill pill--${status.state === 'OPEN' ? 'live' : 'muted'}`}>
        <span className="pill__dot" />
        {stateLabels[status.state] ?? status.state}
      </span>
      <span className="pill pill--warn">
        {qualityLabels[status.dataQuality] ?? status.dataQuality}
      </span>
      <span className="header__timestamp">
        {lastUpdatedAt ? `Updated ${formatTimeOfDay(lastUpdatedAt)}` : 'Loading…'} ·{' '}
        {status.provider}
      </span>
    </div>
  )
}
