import Delta from './Delta.jsx'
import { formatCurrency, formatSignedPercent } from '../lib/format.js'

const ruleStateLabels = {
  TRIGGERED: 'Triggered',
  ARMED: 'Armed',
  NEUTRAL: 'Neutral',
}

const ruleStateTone = {
  TRIGGERED: 'chip--alert',
  ARMED: 'chip--warn',
  NEUTRAL: 'chip--ok',
}

export default function WatchlistPanel({ rows }) {
  return (
    <section className="panel" aria-label="Watchlist">
      <div className="panel__head">
        <h2 className="panel__title">Watchlist</h2>
        <span className="panel__hint">Thresholds and rule state</span>
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
                  <span className="subtle">{row.note}</span>
                </th>
                <td className="num">{formatCurrency(row.quote.price)}</td>
                <td className="num">
                  <Delta value={row.dayChange} showArrow={false}>
                    {formatSignedPercent(row.dayChangePercent)}
                  </Delta>
                </td>
                <td className="num">
                  {formatCurrency(row.buyBelow)}
                  <span className="subtle-inline">
                    {formatSignedPercent(row.distanceToBuy, 1)} away
                  </span>
                </td>
                <td className="num">
                  {formatCurrency(row.sellAbove)}
                  <span className="subtle-inline">
                    {formatSignedPercent(row.distanceToSell, 1)} away
                  </span>
                </td>
                <td>
                  <span className={`chip ${ruleStateTone[row.ruleState] ?? 'chip--ok'}`}>
                    {ruleStateLabels[row.ruleState] ?? row.ruleState}
                  </span>
                </td>
                <td className="num">
                  <span className="score-inline">{row.score}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
