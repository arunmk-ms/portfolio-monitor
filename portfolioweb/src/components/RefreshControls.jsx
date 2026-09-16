import { REFRESH_INTERVALS } from '../hooks/usePortfolioData.js'

function countdownLabel({ autoRefresh, isPaused, isRefreshing, secondsUntilRefresh }) {
  if (isRefreshing) return 'Refreshing…'
  if (!autoRefresh) return 'Auto refresh off'
  if (isPaused) return 'Paused — tab in background'
  if (secondsUntilRefresh === null) return 'Starting…'
  return `Next refresh in ${secondsUntilRefresh}s`
}

export default function RefreshControls({
  onRefresh,
  isRefreshing,
  autoRefresh,
  onAutoRefreshChange,
  intervalMs,
  onIntervalChange,
  secondsUntilRefresh,
  isPaused,
}) {
  return (
    <div className="refresh">
      <button
        type="button"
        className="button button--primary refresh__button"
        onClick={onRefresh}
        disabled={isRefreshing}
      >
        <span className={`refresh__icon ${isRefreshing ? 'refresh__icon--spinning' : ''}`} aria-hidden="true">
          ⟳
        </span>
        {isRefreshing ? 'Refreshing' : 'Refresh'}
      </button>

      <label className="switch">
        <input
          type="checkbox"
          checked={autoRefresh}
          onChange={(event) => onAutoRefreshChange(event.target.checked)}
        />
        <span className="switch__track" aria-hidden="true">
          <span className="switch__thumb" />
        </span>
        <span className="switch__label">Auto</span>
      </label>

      <label className="refresh__interval">
        <span className="visually-hidden">Auto refresh interval</span>
        <select
          className="select"
          value={intervalMs}
          onChange={(event) => onIntervalChange(Number(event.target.value))}
          disabled={!autoRefresh}
        >
          {REFRESH_INTERVALS.map((option) => (
            <option key={option.ms} value={option.ms}>
              every {option.label}
            </option>
          ))}
        </select>
      </label>

      <span className="refresh__status" aria-live="polite">
        {countdownLabel({ autoRefresh, isPaused, isRefreshing, secondsUntilRefresh })}
      </span>
    </div>
  )
}
