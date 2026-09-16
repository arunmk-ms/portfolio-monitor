import { formatDateTime, formatPercent } from '../lib/format.js'

const directionLabels = {
  BUY_CANDIDATE: 'Buy candidate',
  SELL_CANDIDATE: 'Sell candidate',
  REBALANCE: 'Rebalance',
  INFORMATIONAL: 'Informational',
}

const directionTone = {
  BUY_CANDIDATE: 'chip--buy',
  SELL_CANDIDATE: 'chip--sell',
  REBALANCE: 'chip--warn',
  INFORMATIONAL: 'chip--ok',
}

const channelLabels = { EMAIL: 'Email', PUSH: 'Push', SMS: 'SMS' }

function formatFact(key, value) {
  if (typeof value === 'number') {
    if (key === 'allocation' || key === 'target' || key === 'drift') {
      return formatPercent(value, 2)
    }
    return value.toLocaleString('en-US', { maximumFractionDigits: 2 })
  }
  return String(value)
}

export default function AlertHistory({ alerts, signalsById }) {
  return (
    <section className="panel" aria-label="Alert history">
      <div className="panel__head">
        <h2 className="panel__title">Alert history</h2>
        <span className="panel__hint">{alerts.length} alerts sent</span>
      </div>

      <ol className="alerts">
        {alerts.map((alert) => {
          const signal = signalsById.get(alert.signalId)
          const acknowledged = Boolean(alert.acknowledgedAt)

          return (
            <li key={alert.id} className="alert">
              <div className="alert__rail">
                <span className={`alert__dot ${acknowledged ? '' : 'alert__dot--open'}`} />
              </div>

              <div className="alert__body">
                <div className="alert__head">
                  <span className="symbol">{alert.symbol}</span>
                  {signal ? (
                    <span className={`chip ${directionTone[signal.direction] ?? 'chip--ok'}`}>
                      {directionLabels[signal.direction] ?? signal.direction}
                    </span>
                  ) : null}
                  {signal ? <span className="score-inline">Score {signal.score}</span> : null}
                  <span className="alert__time">{formatDateTime(alert.sentAt)}</span>
                </div>

                <p className="alert__summary">{signal?.summary ?? 'Signal detail unavailable.'}</p>

                {signal ? (
                  <ul className="facts">
                    {Object.entries(signal.facts).map(([key, value]) => (
                      <li key={key} className="fact">
                        <span className="fact__key">{key}</span>
                        <span className="fact__value">{formatFact(key, value)}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}

                <p className="alert__meta">
                  {channelLabels[alert.channel] ?? alert.channel}
                  {signal ? ` · rule ${signal.ruleId} v${signal.ruleVersion}` : ''}
                  {' · '}
                  {acknowledged
                    ? `Acknowledged ${formatDateTime(alert.acknowledgedAt)}`
                    : 'Not acknowledged'}
                </p>
              </div>
            </li>
          )
        })}
      </ol>
    </section>
  )
}
