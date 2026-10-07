import Delta from './Delta.jsx'
import EmptyState from './EmptyState.jsx'
import {
  UNKNOWN,
  formatMaybeCurrency,
  formatMaybeNumber,
  formatMaybeSignedPercent,
} from '../lib/format.js'

const ruleStateLabels = {
  TRIGGERED: 'Triggered',
  ARMED: 'Armed',
  NO_RULES: 'No rules',
}

const ruleStateTone = {
  TRIGGERED: 'chip--alert',
  ARMED: 'chip--warn',
  NO_RULES: 'chip--ok',
}

export default function WatchlistPanel({ rows }) {
  if (rows.length === 0) {
    return (
      <section className="panel" aria-label="Watchlist">
        <div className="panel__head">
          <h2 className="panel__title">Watchlist</h2>
        </div>
        <EmptyState
          title="Nothing is being monitored"
          action={
            <a className="button button--primary" href="#/import">
              Import a positions CSV
            </a>
          }
        >
          Imported holdings are added to the watchlist automatically.
        </EmptyState>
      </section>
    )
  }

  const withoutRules = rows.filter((row) => row.ruleState === 'NO_RULES').length

  return (
    <section className="panel" aria-label="Watchlist">
      <div className="panel__head">
        <h2 className="panel__title">Watchlist</h2>
        <span className="panel__hint">
          {rows.length} symbol{rows.length === 1 ? '' : 's'}
        </span>
      </div>

      <div className="table-scroll">
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Symbol</th>
              <th scope="col" className="num">Price</th>
              <th scope="col" className="num">Day</th>
              <th scope="col" className="num">Buy below</th>
              <th scope="col" className="num">Sell above</th>
              <th scope="col">Rule state</th>
              <th scope="col" className="num">Score</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.symbol}>
                <th scope="row">
                  <span className="symbol">{row.symbol}</span>
                  <span className="subtle">
                    {row.ruleCount
                      ? `${row.ruleCount} rule${row.ruleCount === 1 ? '' : 's'} · ${row.source}`
                      : `Added by ${String(row.source).toLowerCase()}`}
                  </span>
                </th>
                <td className="num">{formatMaybeCurrency(row.price)}</td>
                <td className="num">
                  {row.dayChangePercent !== null ? (
                    <Delta value={row.dayChange} showArrow={false}>
                      {formatMaybeSignedPercent(row.dayChangePercent)}
                    </Delta>
                  ) : (
                    UNKNOWN
                  )}
                </td>
                <td className="num">
                  {formatMaybeCurrency(row.buyBelow)}
                  {row.distanceToBuy !== null ? (
                    <span className="subtle-inline">
                      {formatMaybeSignedPercent(row.distanceToBuy, 1)} away
                    </span>
                  ) : null}
                </td>
                <td className="num">
                  {formatMaybeCurrency(row.sellAbove)}
                  {row.distanceToSell !== null ? (
                    <span className="subtle-inline">
                      {formatMaybeSignedPercent(row.distanceToSell, 1)} away
                    </span>
                  ) : null}
                </td>
                <td>
                  <span className={`chip ${ruleStateTone[row.ruleState] ?? 'chip--ok'}`}>
                    {ruleStateLabels[row.ruleState] ?? row.ruleState}
                  </span>
                </td>
                <td className="num">
                  <span className="score-inline">{formatMaybeNumber(row.score, 1)}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {withoutRules > 0 ? (
        <p className="panel__footnote">
          {withoutRules} symbol(s) have no rules, so they are monitored for price only and cannot
          raise a signal.
        </p>
      ) : null}
    </section>
  )
}
