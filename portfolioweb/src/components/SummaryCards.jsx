import Delta from './Delta.jsx'
import {
  UNKNOWN,
  formatCurrency,
  formatMaybeSignedCurrency,
  formatMaybeSignedPercent,
} from '../lib/format.js'

export default function SummaryCards({ summary, openSignalCount, positionCount, counts }) {
  const hasDayChange = summary.dayGainPercent !== null
  const hasCost = summary.unrealizedGain !== null

  return (
    <section className="summary" aria-label="Portfolio summary">
      <article className="card summary__card">
        <p className="card__label">Portfolio value</p>
        <p className="card__value">{formatCurrency(summary.marketValue)}</p>
        <p className="card__hint">
          {positionCount} position{positionCount === 1 ? '' : 's'} · cost{' '}
          {summary.costBasis ? formatCurrency(summary.costBasis) : UNKNOWN}
        </p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Day change</p>
        <p className="card__value">
          {hasDayChange ? (
            <Delta value={summary.dayGain}>{formatMaybeSignedCurrency(summary.dayGain)}</Delta>
          ) : (
            <span className="card__value--muted">{UNKNOWN}</span>
          )}
        </p>
        <p className="card__hint">
          {hasDayChange ? (
            <>
              <Delta value={summary.dayGain} showArrow={false}>
                {formatMaybeSignedPercent(summary.dayGainPercent)}
              </Delta>{' '}
              today
            </>
          ) : (
            'Needs a previous close from the monitoring run'
          )}
        </p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Unrealized gain</p>
        <p className="card__value">
          {hasCost ? (
            <Delta value={summary.unrealizedGain}>
              {formatMaybeSignedCurrency(summary.unrealizedGain)}
            </Delta>
          ) : (
            <span className="card__value--muted">{UNKNOWN}</span>
          )}
        </p>
        <p className="card__hint">
          {hasCost ? (
            <>
              <Delta value={summary.unrealizedGain} showArrow={false}>
                {formatMaybeSignedPercent(summary.unrealizedGainPercent)}
              </Delta>{' '}
              since purchase
            </>
          ) : (
            'No cost basis recorded'
          )}
        </p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Open signals</p>
        <p className="card__value">{openSignalCount}</p>
        <p className="card__hint">
          {counts?.enabledRules
            ? `${counts.enabledRules} active rule${counts.enabledRules === 1 ? '' : 's'} · review only`
            : 'No rules configured yet'}
        </p>
      </article>
    </section>
  )
}
