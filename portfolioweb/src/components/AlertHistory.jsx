import EmptyState from './EmptyState.jsx'
import { formatDateTime } from '../lib/format.js'

const channelLabels = {
  Log: 'Log only',
  Email: 'Email',
  Push: 'Push',
}

const directionLabels = {
  BuyCandidate: 'Buy candidate',
  SellCandidate: 'Sell candidate',
  Rebalance: 'Rebalance',
  Informational: 'Informational',
}

const directionTone = {
  BuyCandidate: 'chip--buy',
  SellCandidate: 'chip--sell',
  Rebalance: 'chip--warn',
  Informational: 'chip--ok',
}

export default function AlertHistory({ alerts, signalsById, hasSignals }) {
  if (alerts.length === 0) {
    return (
      <section className="panel" aria-label="Alert history">
        <div className="panel__head">
          <h2 className="panel__title">Alert history</h2>
        </div>
        <EmptyState title="No alerts sent">
          {hasSignals
            ? 'Signals have fired but no delivery has been recorded yet.'
            : 'Alerts appear here once a rule fires and a notification is attempted.'}
        </EmptyState>
      </section>
    )
  }

  return (
    <section className="panel" aria-label="Alert history">
      <div className="panel__head">
        <h2 className="panel__title">Alert history</h2>
        <span className="panel__hint">
          {alerts.length} alert{alerts.length === 1 ? '' : 's'}
        </span>
      </div>

      <ol className="alerts">
        {alerts.map((alert) => {
          const signal = signalsById.get(alert.signalId)
          const acknowledged = Boolean(alert.acknowledgedAt)
          const failed = Boolean(alert.failureReason)

          return (
            <li key={alert.id} className="alert">
              <div className="alert__rail">
                <span
                  className={`alert__dot ${failed ? 'alert__dot--failed' : acknowledged ? '' : 'alert__dot--open'}`}
                />
              </div>

              <div className="alert__body">
                <div className="alert__head">
                  <span className="symbol">{alert.symbol ?? signal?.symbol ?? 'Unknown'}</span>
                  {signal ? (
                    <span className={`chip ${directionTone[signal.direction] ?? 'chip--ok'}`}>
                      {directionLabels[signal.direction] ?? signal.direction}
                    </span>
                  ) : null}
                  {signal ? (
                    <span className="score-inline">Score {Math.round(signal.score)}</span>
                  ) : null}
                  <span className="alert__time">
                    {alert.sentAt ? formatDateTime(alert.sentAt) : 'Not sent'}
                  </span>
                </div>

                {failed ? (
                  <p className="alert__summary alert__summary--failed">
                    Delivery failed: {alert.failureReason}
                  </p>
                ) : null}

                <p className="alert__meta">
                  {channelLabels[alert.channel] ?? alert.channel}
                  {signal ? ` · ${signal.ruleName ?? 'rule'} v${signal.ruleVersion}` : ''}
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
