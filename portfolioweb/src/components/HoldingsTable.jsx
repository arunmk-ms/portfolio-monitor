import Delta from './Delta.jsx'
import EmptyState from './EmptyState.jsx'
import {
  UNKNOWN,
  formatCurrency,
  formatMaybeCurrency,
  formatMaybeNumber,
  formatMaybePercent,
  formatMaybeSignedCurrency,
  formatMaybeSignedPercent,
} from '../lib/format.js'

export default function HoldingsTable({ positions, totals }) {
  if (positions.length === 0) {
    return (
      <section className="panel" aria-label="Holdings">
        <div className="panel__head">
          <h2 className="panel__title">Holdings</h2>
        </div>
        <EmptyState
          title="No holdings yet"
          action={
            <a className="button button--primary" href="#/import">
              Import a positions CSV
            </a>
          }
        >
          Upload a broker positions export and it will appear here.
        </EmptyState>
      </section>
    )
  }

  return (
    <section className="panel" aria-label="Holdings">
      <div className="panel__head">
        <h2 className="panel__title">Holdings</h2>
        <span className="panel__hint">
          {positions.length} position{positions.length === 1 ? '' : 's'}
        </span>
      </div>

      <div className="table-scroll">
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Symbol</th>
              <th scope="col" className="num">Qty</th>
              <th scope="col" className="num">Avg cost</th>
              <th scope="col" className="num">Price</th>
              <th scope="col" className="num">Day</th>
              <th scope="col" className="num">Market value</th>
              <th scope="col" className="num">Unrealized</th>
              <th scope="col" className="num">Weight</th>
            </tr>
          </thead>
          <tbody>
            {positions.map((position) => (
              <tr key={position.id}>
                <th scope="row">
                  <span className="symbol">
                    {position.symbol}
                    {position.isMonitorable ? null : (
                      <span className="chip chip--ok symbol__tag">{position.positionType}</span>
                    )}
                  </span>
                  <span className="subtle">
                    {position.description ?? 'No description'}
                    {position.account ? ` · ${position.account}` : ''}
                  </span>
                </th>
                <td className="num">{formatMaybeNumber(position.quantity, 4)}</td>
                <td className="num">{formatMaybeCurrency(position.averageCost)}</td>
                <td className="num">{formatMaybeCurrency(position.price)}</td>
                <td className="num">
                  {position.dayChangePercent !== null ? (
                    <Delta value={position.dayChange} showArrow={false}>
                      {formatMaybeSignedPercent(position.dayChangePercent)}
                    </Delta>
                  ) : (
                    UNKNOWN
                  )}
                </td>
                <td className="num">{formatMaybeCurrency(position.marketValue)}</td>
                <td className="num">
                  {position.unrealizedGain !== null ? (
                    <Delta value={position.unrealizedGain} showArrow={false}>
                      {formatMaybeSignedCurrency(position.unrealizedGain)}
                      <span className="subtle-inline">
                        {formatMaybeSignedPercent(position.unrealizedGainPercent)}
                      </span>
                    </Delta>
                  ) : (
                    UNKNOWN
                  )}
                </td>
                <td className="num">{formatMaybePercent(position.allocation, 1)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row">Total</th>
              <td className="num" colSpan={4} />
              <td className="num">{formatCurrency(totals.marketValue)}</td>
              <td className="num">
                {totals.unrealizedGain !== null ? (
                  <Delta value={totals.unrealizedGain} showArrow={false}>
                    {formatMaybeSignedCurrency(totals.unrealizedGain)}
                    <span className="subtle-inline">
                      {formatMaybeSignedPercent(totals.unrealizedGainPercent)}
                    </span>
                  </Delta>
                ) : (
                  UNKNOWN
                )}
              </td>
              <td className="num">100.0%</td>
            </tr>
          </tfoot>
        </table>
      </div>

      {totals.positionsWithoutCost > 0 ? (
        <p className="panel__footnote">
          {totals.positionsWithoutCost} position(s) have no cost basis, so unrealized gain excludes
          them.
        </p>
      ) : null}
    </section>
  )
}
