import Delta from './Delta.jsx'
import {
  formatCurrency,
  formatPercent,
  formatSignedCurrency,
  formatSignedPercent,
} from '../lib/format.js'

export default function HoldingsTable({ positions, totals }) {
  return (
    <section className="panel" aria-label="Holdings">
      <div className="panel__head">
        <h2 className="panel__title">Holdings</h2>
        <span className="panel__hint">{positions.length} positions</span>
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
                  <span className="symbol">{position.symbol}</span>
                  <span className="subtle">{position.account}</span>
                </th>
                <td className="num">{position.quantity}</td>
                <td className="num">{formatCurrency(position.averageCost)}</td>
                <td className="num">{formatCurrency(position.quote.price)}</td>
                <td className="num">
                  <Delta value={position.dayChange} showArrow={false}>
                    {formatSignedPercent(position.dayChangePercent)}
                  </Delta>
                </td>
                <td className="num">{formatCurrency(position.marketValue)}</td>
                <td className="num">
                  <Delta value={position.unrealizedGain} showArrow={false}>
                    {formatSignedCurrency(position.unrealizedGain)}
                    <span className="subtle-inline">
                      {formatSignedPercent(position.unrealizedGainPercent)}
                    </span>
                  </Delta>
                </td>
                <td className="num">{formatPercent(position.allocation, 1)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <th scope="row">Total</th>
              <td className="num" colSpan={4} />
              <td className="num">{formatCurrency(totals.marketValue)}</td>
              <td className="num">
                <Delta value={totals.unrealizedGain} showArrow={false}>
                  {formatSignedCurrency(totals.unrealizedGain)}
                  <span className="subtle-inline">
                    {formatSignedPercent(totals.unrealizedGainPercent)}
                  </span>
                </Delta>
              </td>
              <td className="num">100.0%</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </section>
  )
}
