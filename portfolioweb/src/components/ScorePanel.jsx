import EmptyState from './EmptyState.jsx'
import { formatDateTime, formatMaybeNumber } from '../lib/format.js'

function scoreTone(score) {
  if (score >= 70) return 'high'
  if (score >= 45) return 'medium'
  return 'low'
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

function formatFactValue(value) {
  return typeof value === 'number' ? formatMaybeNumber(value, 2) : String(value)
}

/**
 * Open signals with the facts that produced them.
 *
 * The score and every fact come from the rule engine's stored evaluation rather than being
 * recomputed here, so what the user reads is exactly what fired.
 */
export default function ScorePanel({ breakdowns, onReview, pendingId, hasRules }) {
  if (breakdowns.length === 0) {
    return (
      <section className="panel" aria-label="Opportunity scores">
        <div className="panel__head">
          <h2 className="panel__title">Opportunity scores</h2>
        </div>
        <EmptyState title="No open signals">
          {hasRules
            ? 'Rules are active but nothing has crossed a threshold yet.'
            : 'Scores are produced by the rule engine. Add a watch rule to start evaluating.'}
        </EmptyState>
      </section>
    )
  }

  return (
    <section className="panel" aria-label="Opportunity scores">
      <div className="panel__head">
        <h2 className="panel__title">Opportunity scores</h2>
        <span className="panel__hint">0–100 · decision support only</span>
      </div>

      <div className="scores">
        {breakdowns.map((entry) => {
          const tone = scoreTone(entry.score)
          const isPending = pendingId === entry.id

          return (
            <article key={entry.id} className="score">
              <div className="score__head">
                <div
                  className={`score__gauge score__gauge--${tone}`}
                  style={{ '--score': entry.score }}
                >
                  <span className="score__gauge-value">{Math.round(entry.score)}</span>
                </div>
                <div className="score__headText">
                  <p className="symbol">{entry.symbol}</p>
                  <p className="subtle">
                    {entry.ruleName ?? 'Rule'} v{entry.ruleVersion} ·{' '}
                    {formatDateTime(entry.createdAt)}
                  </p>
                </div>
                <span className={`chip ${directionTone[entry.direction] ?? 'chip--ok'}`}>
                  {directionLabels[entry.direction] ?? entry.direction}
                </span>
              </div>

              {entry.factors.length ? (
                <ul className="facts">
                  {entry.factors.map((factor) => (
                    <li key={factor.key} className="fact">
                      <span className="fact__key">{factor.label}</span>
                      <span className="fact__value">{formatFactValue(factor.value)}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="subtle">No supporting facts were recorded for this signal.</p>
              )}

              <div className="score__actions">
                <button
                  type="button"
                  className="button"
                  onClick={() => onReview(entry.id, 'Acknowledged')}
                  disabled={isPending}
                >
                  {isPending ? 'Saving…' : 'Acknowledge'}
                </button>
                <button
                  type="button"
                  className="button button--ghost"
                  onClick={() => onReview(entry.id, 'Ignored')}
                  disabled={isPending}
                >
                  Ignore
                </button>
              </div>
            </article>
          )
        })}
      </div>
    </section>
  )
}
