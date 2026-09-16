import { useMemo, useRef, useState } from 'react'
import Delta from './Delta.jsx'
import {
  parsePositionsCsv,
  summarizeImport,
  toImportPayload,
} from '../lib/importPositions.js'
import { uploadPositionsCsv } from '../lib/portfolioApi.js'
import {
  formatCurrency,
  formatSignedCurrency,
  formatSignedPercent,
} from '../lib/format.js'

const MAX_BYTES = 5 * 1024 * 1024

function formatQuantity(value) {
  return value.toLocaleString('en-US', { maximumFractionDigits: 4 })
}

function formatMaybeCurrency(value) {
  return value === null || value === undefined ? '—' : formatCurrency(value)
}

export default function ImportPage() {
  const [file, setFile] = useState(null)
  const [result, setResult] = useState(null)
  const [fatalError, setFatalError] = useState(null)
  const [isParsing, setIsParsing] = useState(false)
  const [isDragging, setIsDragging] = useState(false)
  const [saveState, setSaveState] = useState({ status: 'idle', error: null, result: null })
  const inputRef = useRef(null)
  const dragDepth = useRef(0)
  const saveInFlight = useRef(false)

  const summary = useMemo(() => (result?.ok ? summarizeImport(result) : null), [result])
  const payload = useMemo(() => (result?.ok ? toImportPayload(result) : null), [result])

  async function handleFile(nextFile) {
    if (!nextFile) return

    setFatalError(null)
    setResult(null)
    setFile(null)
    setSaveState({ status: 'idle', error: null, result: null })

    // Trust the extension rather than the MIME type: browsers report CSV files
    // inconsistently (text/csv, application/vnd.ms-excel, text/plain), and
    // accepting text/plain would let unrelated .txt files through to the parser.
    if (!nextFile.name.toLowerCase().endsWith('.csv')) {
      setFatalError(`"${nextFile.name}" is not a CSV file. Export your positions as CSV and try again.`)
      return
    }

    if (nextFile.size > MAX_BYTES) {
      setFatalError(`"${nextFile.name}" is larger than 5 MB. That is far bigger than a positions export.`)
      return
    }

    if (nextFile.size === 0) {
      setFatalError(`"${nextFile.name}" is empty.`)
      return
    }

    setIsParsing(true)
    try {
      const text = await nextFile.text()
      setResult(parsePositionsCsv(text, { sourceName: nextFile.name }))
      setFile(nextFile)
    } catch (cause) {
      setFatalError(`Could not read the file: ${cause.message}`)
    } finally {
      setIsParsing(false)
    }
  }

  async function handleSave() {
    // State updates are batched, so an isSaving check alone would let two rapid
    // clicks both start an upload and create duplicate import records.
    if (saveInFlight.current || !file) return

    saveInFlight.current = true
    setSaveState({ status: 'saving', error: null, result: null })

    try {
      const saved = await uploadPositionsCsv(file)
      setSaveState({ status: 'saved', error: null, result: saved })
    } catch (cause) {
      setSaveState({ status: 'error', error: cause.message, result: null })
    } finally {
      saveInFlight.current = false
    }
  }

  function handleDrop(event) {
    event.preventDefault()
    dragDepth.current = 0
    setIsDragging(false)
    handleFile(event.dataTransfer.files?.[0])
  }

  function reset() {
    setResult(null)
    setFile(null)
    setFatalError(null)
    setSaveState({ status: 'idle', error: null, result: null })
    if (inputRef.current) inputRef.current.value = ''
  }

  function downloadPayload() {
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${result.sourceName.replace(/\.csv$/i, '')}-normalized.json`
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  }

  const errors = result?.issues.filter((issue) => issue.severity === 'error') ?? []
  const warnings = result?.issues.filter((issue) => issue.severity === 'warning') ?? []
  const notes = result?.issues.filter((issue) => issue.severity === 'info') ?? []

  return (
    <main className="app__main">
      <section className="panel" aria-label="Upload positions">
        <div className="panel__head">
          <h2 className="panel__title">Import holdings from CSV</h2>
          <span className="panel__hint">Positions export from your broker</span>
        </div>

        <div
          className={`drop ${isDragging ? 'drop--active' : ''}`}
          onDragEnter={(event) => {
            event.preventDefault()
            dragDepth.current += 1
            setIsDragging(true)
          }}
          onDragOver={(event) => event.preventDefault()}
          onDragLeave={(event) => {
            event.preventDefault()
            dragDepth.current -= 1
            if (dragDepth.current <= 0) setIsDragging(false)
          }}
          onDrop={handleDrop}
        >
          <span className="drop__icon" aria-hidden="true">
            ⇪
          </span>
          <p className="drop__title">Drop a CSV here</p>
          <p className="subtle drop__subtitle">
            or choose a file. It is previewed locally first, and only sent to the portfolio API
            when you choose to save.
          </p>

          <input
            ref={inputRef}
            id="csv-input"
            className="visually-hidden"
            type="file"
            accept=".csv,text/csv"
            onChange={(event) => handleFile(event.target.files?.[0])}
          />
          <label className="button button--primary drop__button" htmlFor="csv-input">
            {isParsing ? 'Reading…' : 'Choose CSV file'}
          </label>
        </div>

        <p className="panel__footnote">
          Account numbers are ignored on import and are never read, displayed, or exported.
        </p>
      </section>

      {fatalError ? (
        <div className="banner banner--error" role="alert">
          <span>{fatalError}</span>
          <button type="button" className="button button--ghost" onClick={reset}>
            Dismiss
          </button>
        </div>
      ) : null}

      {result && !result.ok ? (
        <div className="banner banner--error" role="alert">
          <span>{errors[0]?.message ?? 'This file could not be imported.'}</span>
          <button type="button" className="button button--ghost" onClick={reset}>
            Try another file
          </button>
        </div>
      ) : null}

      {result?.ok && summary ? (
        <>
          <section className="summary" aria-label="Import summary">
            <article className="card summary__card">
              <p className="card__label">Positions found</p>
              <p className="card__value">{result.stats.positions}</p>
              <p className="card__hint">
                from <span className="card__file" title={result.sourceName}>{result.sourceName}</span>
                {result.stats.skipped ? ` · ${result.stats.skipped} row(s) skipped` : ''}
              </p>
            </article>

            <article className="card summary__card">
              <p className="card__label">Market value</p>
              <p className="card__value">{formatCurrency(summary.marketValue)}</p>
              <p className="card__hint">
                {summary.cashValue
                  ? `plus ${formatCurrency(summary.cashValue)} cash`
                  : 'no cash rows'}
              </p>
            </article>

            <article className="card summary__card">
              <p className="card__label">Cost basis</p>
              <p className="card__value">{formatCurrency(summary.costBasis)}</p>
              <p className="card__hint">
                {summary.withoutCost
                  ? `${summary.withoutCost} position(s) missing cost`
                  : 'complete for all positions'}
              </p>
            </article>

            <article className="card summary__card">
              <p className="card__label">Unrealized</p>
              <p className="card__value">
                <Delta value={summary.unrealizedGain}>
                  {formatSignedCurrency(summary.unrealizedGain)}
                </Delta>
              </p>
              <p className="card__hint">
                {summary.unrealizedGainPercent === null ? (
                  'cost basis unknown'
                ) : (
                  <Delta value={summary.unrealizedGain} showArrow={false}>
                    {formatSignedPercent(summary.unrealizedGainPercent)}
                  </Delta>
                )}
              </p>
            </article>
          </section>

          {saveState.status === 'saved' ? (
            <div className="banner banner--success" role="status">
              <div>
                <strong>Saved to your portfolio.</strong>{' '}
                {saveState.result.positionsImported} position(s) stored
                {saveState.result.symbolsAddedToWatchlist
                  ? `, ${saveState.result.symbolsAddedToWatchlist} new symbol(s) added to the watchlist`
                  : ''}
                {saveState.result.cashRowsSkipped
                  ? `, ${saveState.result.cashRowsSkipped} cash/other row(s) stored but not monitored`
                  : ''}
                . Import #{saveState.result.importId}.
              </div>
              <div className="banner__actions">
                <button type="button" className="button" onClick={downloadPayload}>
                  Download normalized JSON
                </button>
              </div>
            </div>
          ) : (
            <div className={`banner ${saveState.status === 'error' ? 'banner--error' : 'banner--action'}`} role={saveState.status === 'error' ? 'alert' : undefined}>
              <div>
                {saveState.status === 'error' ? (
                  <>
                    <strong>Could not save.</strong> {saveState.error}
                  </>
                ) : (
                  <>
                    Reviewed and correct? Send this file to the portfolio API to store the
                    accounts, holdings, and monitored symbols.
                  </>
                )}
              </div>
              <div className="banner__actions">
                <button type="button" className="button" onClick={downloadPayload}>
                  Download normalized JSON
                </button>
                <button
                  type="button"
                  className="button button--primary"
                  onClick={handleSave}
                  disabled={saveState.status === 'saving'}
                >
                  {saveState.status === 'saving'
                    ? 'Saving…'
                    : saveState.status === 'error'
                      ? 'Retry save'
                      : 'Save to portfolio'}
                </button>
              </div>
            </div>
          )}

          <section className="panel" aria-label="Parsed positions">
            <div className="panel__head">
              <h2 className="panel__title">Preview</h2>
              <span className="panel__hint">{result.stats.positions} positions</span>
            </div>

            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Symbol</th>
                    <th scope="col" className="num">Qty</th>
                    <th scope="col" className="num">Last price</th>
                    <th scope="col" className="num">Value</th>
                    <th scope="col" className="num">Avg cost</th>
                    <th scope="col" className="num">Cost basis</th>
                    <th scope="col" className="num">Total G/L</th>
                  </tr>
                </thead>
                <tbody>
                  {result.positions.map((position) => (
                    <tr key={`${position.symbol}-${position.line}`}>
                      <th scope="row">
                        <span className="symbol">{position.symbol}</span>
                        <span className="subtle">
                          {position.description}
                          {position.account ? ` · ${position.account}` : ''}
                        </span>
                      </th>
                      <td className="num">{formatQuantity(position.quantity)}</td>
                      <td className="num">{formatMaybeCurrency(position.lastPrice)}</td>
                      <td className="num">{formatMaybeCurrency(position.currentValue)}</td>
                      <td className="num">{formatMaybeCurrency(position.averageCost)}</td>
                      <td className="num">{formatMaybeCurrency(position.costBasisTotal)}</td>
                      <td className="num">
                        {position.totalGainLoss === null ? (
                          '—'
                        ) : (
                          <Delta value={position.totalGainLoss} showArrow={false}>
                            {formatSignedCurrency(position.totalGainLoss)}
                            {position.totalGainLossPercent === null ? null : (
                              <span className="subtle-inline">
                                {formatSignedPercent(position.totalGainLossPercent)}
                              </span>
                            )}
                          </Delta>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {result.cash.length ? (
              <p className="panel__footnote">
                Cash and money-market rows are listed separately and excluded from the positions
                table:{' '}
                {result.cash
                  .map((entry) => `${entry.symbol} ${formatMaybeCurrency(entry.currentValue)}`)
                  .join(', ')}
                .
              </p>
            ) : null}
          </section>

          {warnings.length || notes.length || result.ignoredColumns.length ? (
            <section className="panel" aria-label="Import notes">
              <div className="panel__head">
                <h2 className="panel__title">Notes</h2>
                <span className="panel__hint">
                  {[
                    result.ignoredColumns.length && `${result.ignoredColumns.length} ignored column(s)`,
                    warnings.length && `${warnings.length} warning(s)`,
                    notes.length && `${notes.length} note(s)`,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </span>
              </div>

              <ul className="issues">
                {result.ignoredColumns.map((column) => (
                  <li key={column} className="issue">
                    <span className="chip chip--ok">ignored</span>
                    <span>
                      Column <code>{column}</code> was not read.
                    </span>
                  </li>
                ))}
                {warnings.map((issue, i) => (
                  <li key={`w-${i}`} className="issue">
                    <span className="chip chip--warn">
                      {issue.line ? `line ${issue.line}` : 'warning'}
                    </span>
                    <span>{issue.message}</span>
                  </li>
                ))}
                {notes.map((issue, i) => (
                  <li key={`n-${i}`} className="issue">
                    <span className="chip chip--ok">
                      {issue.line ? `line ${issue.line}` : 'note'}
                    </span>
                    <span>{issue.message}</span>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <section className="panel" aria-label="Import payload">
            <div className="panel__head">
              <h2 className="panel__title">Payload for review</h2>
              <span className="panel__hint">Local preview of what the file contains</span>
            </div>
            <details className="payload">
              <summary className="payload__summary">Show normalized JSON</summary>
              <pre className="payload__code">{JSON.stringify(payload, null, 2)}</pre>
            </details>
            <p className="panel__footnote">
              The file itself is sent to the API, which re-parses it server-side and stores the
              result. This preview is a local check, not the source of truth. Contains no
              account-identifying fields.
            </p>
          </section>

          <div className="import__footer">
            <button type="button" className="button button--ghost" onClick={reset}>
              Import a different file
            </button>
          </div>
        </>
      ) : null}
    </main>
  )
}
