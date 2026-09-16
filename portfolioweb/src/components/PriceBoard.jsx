import Delta from './Delta.jsx'
import {
  formatCompactNumber,
  formatCurrency,
  formatSignedCurrency,
  formatSignedPercent,
} from '../lib/format.js'

function rangePosition(quote) {
  const span = quote.fiftyTwoWeekHigh - quote.fiftyTwoWeekLow
  if (span <= 0) return 0
  const ratio = (quote.price - quote.fiftyTwoWeekLow) / span
  return Math.min(100, Math.max(0, ratio * 100))
}

export default function PriceBoard({ quotes }) {
  return (
    <section className="panel" aria-label="Prices">
      <div className="panel__head">
        <h2 className="panel__title">Prices</h2>
        <span className="panel__hint">Last snapshot per symbol</span>
      </div>

      <div className="quotes">
        {quotes.map((quote) => {
          const dayChange = quote.price - quote.previousClose
          const dayChangePercent = dayChange / quote.previousClose

          return (
            <article key={quote.symbol} className="quote">
              <div className="quote__head">
                <div>
                  <p className="quote__symbol">{quote.symbol}</p>
                  <p className="quote__name">{quote.name}</p>
                </div>
                <div className="quote__price">
                  <p className="quote__last">{formatCurrency(quote.price)}</p>
                  <p>
                    <Delta value={dayChange}>
                      {formatSignedCurrency(dayChange)} ({formatSignedPercent(dayChangePercent)})
                    </Delta>
                  </p>
                </div>
              </div>

              <div className="quote__range">
                <div className="quote__range-track">
                  <span
                    className="quote__range-marker"
                    style={{ left: `${rangePosition(quote)}%` }}
                  />
                </div>
                <div className="quote__range-labels">
                  <span>52w low {formatCurrency(quote.fiftyTwoWeekLow)}</span>
                  <span>52w high {formatCurrency(quote.fiftyTwoWeekHigh)}</span>
                </div>
              </div>

              <dl className="quote__stats">
                <div>
                  <dt>Day range</dt>
                  <dd>
                    {formatCurrency(quote.dayLow)} – {formatCurrency(quote.dayHigh)}
                  </dd>
                </div>
                <div>
                  <dt>Volume</dt>
                  <dd>
                    {formatCompactNumber(quote.volume)} vs {formatCompactNumber(quote.averageVolume)} avg
                  </dd>
                </div>
                <div>
                  <dt>RSI (14)</dt>
                  <dd>{quote.rsi14.toFixed(1)}</dd>
                </div>
                <div>
                  <dt>SMA 50 / 200</dt>
                  <dd>
                    {formatCurrency(quote.sma50)} / {formatCurrency(quote.sma200)}
                  </dd>
                </div>
              </dl>
            </article>
          )
        })}
      </div>
    </section>
  )
}
