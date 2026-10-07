import Delta from './Delta.jsx'
import EmptyState from './EmptyState.jsx'
import {
  UNKNOWN,
  formatCurrency,
  formatMaybeCompactNumber,
  formatMaybeCurrency,
  formatMaybeNumber,
  formatMaybeSignedCurrency,
  formatMaybeSignedPercent,
} from '../lib/format.js'

const sourceLabels = {
  provider: 'Market data',
  import: 'From import',
}

export default function PriceBoard({ quotes }) {
  if (quotes.length === 0) {
    return (
      <section className="panel" aria-label="Prices">
        <div className="panel__head">
          <h2 className="panel__title">Prices</h2>
        </div>
        <EmptyState title="No prices yet">
          Import a positions file, or let the monitoring run fetch quotes for your watchlist.
        </EmptyState>
      </section>
    )
  }

  return (
    <section className="panel" aria-label="Prices">
      <div className="panel__head">
        <h2 className="panel__title">Prices</h2>
        <span className="panel__hint">Latest stored price per symbol</span>
      </div>

      <div className="quotes">
        {quotes.map((quote) => {
          const dayChange =
            quote.previousClose != null ? quote.price - quote.previousClose : null
          const dayChangePercent =
            dayChange !== null && quote.previousClose ? dayChange / quote.previousClose : null

          return (
            <article key={quote.symbol} className="quote">
              <div className="quote__head">
                <div>
                  <p className="quote__symbol">{quote.symbol}</p>
                  <p className="quote__name">{quote.name ?? 'No description'}</p>
                </div>
                <div className="quote__price">
                  <p className="quote__last">{formatCurrency(quote.price)}</p>
                  <p>
                    {dayChange !== null ? (
                      <Delta value={dayChange}>
                        {formatMaybeSignedCurrency(dayChange)} (
                        {formatMaybeSignedPercent(dayChangePercent)})
                      </Delta>
                    ) : (
                      <span className="subtle-inline">No previous close</span>
                    )}
                  </p>
                </div>
              </div>

              <div className="quote__tags">
                <span
                  className={`chip ${quote.source === 'provider' ? 'chip--ok' : 'chip--warn'}`}
                >
                  {sourceLabels[quote.source] ?? quote.source}
                </span>
                {quote.latestTradingDay ? (
                  <span className="subtle-inline">as of {quote.latestTradingDay}</span>
                ) : null}
              </div>

              <dl className="quote__stats">
                <div>
                  <dt>Day range</dt>
                  <dd>
                    {quote.dayLow != null && quote.dayHigh != null
                      ? `${formatCurrency(quote.dayLow)} – ${formatCurrency(quote.dayHigh)}`
                      : UNKNOWN}
                  </dd>
                </div>
                <div>
                  <dt>Volume</dt>
                  <dd>{formatMaybeCompactNumber(quote.volume)}</dd>
                </div>
                <div>
                  <dt>RSI (14)</dt>
                  <dd>{formatMaybeNumber(quote.rsi14, 1)}</dd>
                </div>
                <div>
                  <dt>SMA 50 / 200</dt>
                  <dd>
                    {formatMaybeCurrency(quote.sma50)} / {formatMaybeCurrency(quote.sma200)}
                  </dd>
                </div>
              </dl>

              {quote.historyPoints < 15 ? (
                <p className="subtle">
                  {quote.historyPoints === 0
                    ? 'No price history stored yet, so indicators are unavailable.'
                    : `${quote.historyPoints} snapshot(s) stored; indicators need more history.`}
                </p>
              ) : null}
            </article>
          )
        })}
      </div>
    </section>
  )
}
