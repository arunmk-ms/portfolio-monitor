import Delta from './Delta.jsx'
import { formatCurrency, formatPercent, formatSignedPercent } from '../lib/format.js'

const DRIFT_LIMIT = 0.05

export default function AllocationPanel({ positions, totalValue }) {
  return (
    <section className="panel" aria-label="Allocation">
      <div className="panel__head">
        <h2 className="panel__title">Allocation</h2>
        <span className="panel__hint">Actual vs target · drift limit {formatPercent(DRIFT_LIMIT, 0)}</span>
      </div>

      <div className="allocation__stack" role="img" aria-label="Allocation by symbol">
        {positions.map((position, index) => (
          <span
            key={position.id}
            className={`allocation__slice allocation__slice--${index % 4}`}
            style={{ width: `${position.allocation * 100}%` }}
            title={`${position.symbol} ${formatPercent(position.allocation, 1)}`}
          />
        ))}
      </div>

      <ul className="allocation__list">
        {positions.map((position, index) => {
          const overLimit = Math.abs(position.allocationDrift) > DRIFT_LIMIT

          return (
            <li key={position.id} className="allocation__row">
              <div className="allocation__label">
                <span className={`swatch swatch--${index % 4}`} />
                <span className="symbol">{position.symbol}</span>
                <span className="subtle">{formatCurrency(position.marketValue)}</span>
              </div>

              <div className="allocation__bars">
                <div className="allocation__bar">
                  <span
                    className={`allocation__bar-fill allocation__bar-fill--${index % 4}`}
                    style={{ width: `${position.allocation * 100}%` }}
                  />
                  <span
                    className="allocation__target"
                    style={{ left: `${position.targetAllocation * 100}%` }}
                    title={`Target ${formatPercent(position.targetAllocation, 0)}`}
                  />
                </div>
              </div>

              <div className="allocation__values">
                <span className="allocation__actual">{formatPercent(position.allocation, 1)}</span>
                <span className={`chip ${overLimit ? 'chip--warn' : 'chip--ok'}`}>
                  <Delta value={position.allocationDrift} showArrow={false}>
                    {formatSignedPercent(position.allocationDrift, 1)}
                  </Delta>
                </span>
              </div>
            </li>
          )
        })}
      </ul>

      <p className="panel__footnote">
        Total allocated {formatCurrency(totalValue)}. Drift is measured against each position&apos;s
        target weight.
      </p>
    </section>
  )
}
