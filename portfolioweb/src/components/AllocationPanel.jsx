import EmptyState from './EmptyState.jsx'
import { formatCurrency, formatMaybeCurrency, formatMaybePercent } from '../lib/format.js'

export default function AllocationPanel({ positions, totalValue }) {
  const sized = positions.filter((position) => position.allocation !== null)

  if (sized.length === 0) {
    return (
      <section className="panel" aria-label="Allocation">
        <div className="panel__head">
          <h2 className="panel__title">Allocation</h2>
        </div>
        <EmptyState title="Nothing to allocate">
          Allocation is calculated once positions have a market value.
        </EmptyState>
      </section>
    )
  }

  const hasLimits = sized.some((position) => position.allocationLimit !== null)

  return (
    <section className="panel" aria-label="Allocation">
      <div className="panel__head">
        <h2 className="panel__title">Allocation</h2>
        <span className="panel__hint">
          {hasLimits ? 'Actual vs rule limit' : 'Share of portfolio value'}
        </span>
      </div>

      <div className="allocation__stack" role="img" aria-label="Allocation by symbol">
        {sized.map((position, index) => (
          <span
            key={position.id}
            className={`allocation__slice allocation__slice--${index % 4}`}
            style={{ width: `${position.allocation * 100}%` }}
            title={`${position.symbol} ${formatMaybePercent(position.allocation, 1)}`}
          />
        ))}
      </div>

      <ul className="allocation__list">
        {sized.map((position, index) => (
          <li key={position.id} className="allocation__row">
            <div className="allocation__label">
              <span className={`swatch swatch--${index % 4}`} />
              <span className="symbol">{position.symbol}</span>
              <span className="subtle">{formatMaybeCurrency(position.marketValue)}</span>
            </div>

            <div className="allocation__bars">
              <div className="allocation__bar">
                <span
                  className={`allocation__bar-fill allocation__bar-fill--${index % 4}`}
                  style={{ width: `${position.allocation * 100}%` }}
                />
                {position.allocationLimit !== null ? (
                  <span
                    className="allocation__target"
                    style={{ left: `${Math.min(100, position.allocationLimit * 100)}%` }}
                    title={`Rule limit ${formatMaybePercent(position.allocationLimit, 0)}`}
                  />
                ) : null}
              </div>
            </div>

            <div className="allocation__values">
              <span className="allocation__actual">
                {formatMaybePercent(position.allocation, 1)}
              </span>
              {position.allocationLimit !== null ? (
                <span className={`chip ${position.allocationOverLimit ? 'chip--warn' : 'chip--ok'}`}>
                  {position.allocationOverLimit ? 'over limit' : 'within limit'}
                </span>
              ) : null}
            </div>
          </li>
        ))}
      </ul>

      <p className="panel__footnote">
        Total allocated {formatCurrency(totalValue)}.{' '}
        {hasLimits
          ? 'Limits come from enabled allocation rules.'
          : 'Add an allocation rule to track drift against a limit.'}
      </p>
    </section>
  )
}
