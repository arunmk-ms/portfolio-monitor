import Delta from './Delta.jsx'
import {
  formatCurrency,
  formatSignedCurrency,
  formatSignedPercent,
} from '../lib/format.js'

export default function SummaryCards({ summary, openSignalCount, positionCount }) {
  return (
    <section className="summary" aria-label="Portfolio summary">
      <article className="card summary__card">
        <p className="card__label">Portfolio value</p>
        <p className="card__value">{formatCurrency(summary.marketValue)}</p>
        <p className="card__hint">{positionCount} positions · cost {formatCurrency(summary.costBasis)}</p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Day change</p>
        <p className="card__value">
          <Delta value={summary.dayGain}>{formatSignedCurrency(summary.dayGain)}</Delta>
        </p>
        <p className="card__hint">
          <Delta value={summary.dayGain} showArrow={false}>
            {formatSignedPercent(summary.dayGainPercent)}
          </Delta>{' '}
          today
        </p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Unrealized gain</p>
        <p className="card__value">
          <Delta value={summary.unrealizedGain}>
            {formatSignedCurrency(summary.unrealizedGain)}
          </Delta>
        </p>
        <p className="card__hint">
          <Delta value={summary.unrealizedGain} showArrow={false}>
            {formatSignedPercent(summary.unrealizedGainPercent)}
          </Delta>{' '}
          since purchase
        </p>
      </article>

      <article className="card summary__card">
        <p className="card__label">Open signals</p>
        <p className="card__value">{openSignalCount}</p>
        <p className="card__hint">Awaiting review · recommendations only</p>
      </article>
    </section>
  )
}
